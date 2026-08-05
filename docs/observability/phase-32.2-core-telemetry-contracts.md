# Phase 32.2 — Core telemetry contracts

Implementation of the foundational telemetry contracts defined in Phase 32.1.
This phase adds **only** the contract layer: one `ActivitySource`, one `Meter`,
constant name/tag catalogs, bounded value sets, enum-backed mappers, a
failure-category mapper, and a tag validator — plus contract/architecture
tests. **No workflows are instrumented, no exporters or listeners are created,
no OpenTelemetry packages are added, and no DI/appsettings change.**

## 1. Files added (production)

All under `IranDirect.Core/Observability/Telemetry/`:

| File | Responsibility |
|---|---|
| `IranDirectTelemetry.cs` | Single static `ActivitySource` + `Meter` (name `IranDirect.Core`, version from Core assembly). No listeners/exporters. |
| `IranDirectActivityNames.cs` | 28 constant span names (5 roots + 23 children). |
| `IranDirectMetricNames.cs` | 31 constant metric names (counters, histograms, gauges). Names-only; no instruments created. |
| `IranDirectTagNames.cs` | 11 approved tag names + 18-entry prohibited tag list. |
| `IranDirectTagValues.cs` | Bounded value constants for every approved tag. |
| `TelemetryOutcome.cs` | `TelemetryOutcome` enum (success/failure/cancelled/timeout/no_change/unknown). |
| `TelemetryFailureCategory.cs` | `TelemetryFailureCategory` enum (io/timeout/cancellation/http/dns/routing/serialization/invalid_response/unknown). |
| `TelemetryOutcomeMapper.cs` | Exhaustive switch mappings: `RuntimeExecutionResultStatus`, `CycleCompletionStatus`, `DiagnosticSeverity`, `CustomRouteDnsCacheState`, `IranDirectCommand` → bounded strings. |
| `TelemetryFailureCategoryMapper.cs` | Exception-type and `FaultInjectionPoint` → `TelemetryFailureCategory`. |
| `TelemetryTagValidator.cs` | Static catalog: `IsApprovedTagName`, `IsProhibitedTagName`, `IsValidBoundedValue`. No reflection on hot path. |

## 2. ActivitySource / Meter definition

```csharp
public static class IranDirectTelemetry
{
    public const string SourceName = "IranDirect.Core";
    public static string Version { get; } = ResolveVersion(); // Core assembly version
    public static ActivitySource ActivitySource { get; } = new(SourceName, Version);
    public static Meter Meter { get; } = new(SourceName, Version);
}
```

- Version derives from `typeof(IranDirectTelemetry).Assembly.GetName().Version`
  (with informational-version fallback, then `"1.0.0"`), so it tracks the
  shipping binary and never diverges from a hand-maintained string.
- Both instances are `static readonly`, process-wide, and allocation-free to
  read after static init (verified by `TelemetryAllocationTests`).

## 3. Span-name catalog

Root: `IranDirect.RuntimeCycle`, `IranDirect.PrefixUpdateCheck`,
`IranDirect.CustomRouteRefresh`, `IranDirect.IpcRequest`,
`IranDirect.SupportBundleExport`.

Children (23): `Runtime.{Observe,BuildDecision,BuildPreview,PlanChanges,Execute,
PersistInventory}`, `Routes.{Enumerate,Create,Delete}`, `Prefix.{HttpHead,
HttpGet,Compare,PersistMetadata}`, `Dns.{CacheRead,Resolve,CacheWrite}`,
`Ipc.{Connect,Send,Dispatch,Receive}`, `Support.{CaptureSnapshot,Serialize,
WriteJson,CreateZip}`.

No dynamic names; no identities/prefixes/domains/paths/IDs in names.

## 4. Metric-name catalog

31 constants under the `irandirect.` prefix: 14 counters, 11 histograms (9 end
in `.duration`), 6 observable-gauges. Instruments are **not** created this phase
— names-only contract, per the brief and to avoid hot-path allocation.

## 5. Approved / prohibited tags

Approved (11): `operation, outcome, trigger, route_kind, change_kind, source,
cache_state, ipc_command, diagnostic_severity, service_state, failure_category`.

Prohibited (18): `destination_prefix, gateway, next_hop, interface_index,
interface_name, domain, dns_domain, output_path, file_path, pipe_payload,
machine_name, user_name, exception_message, url, endpoint, route_identity,
diagnostic_id, execution_step_identity`. (Note: `endpoint` exists in the
prohibited *name* list as a tag name, but `route_kind=endpoint` is a legitimate
bounded value in `IranDirectTagValues`; the architecture test excludes value/
list-definition files from the prohibited-scan accordingly.)

## 6. Bounded values

All enumerated in `IranDirectTagValues`; `operation` and `ipc_command` are
validated via their dedicated mappers rather than the generic validator. No
`ToString()`-as-mapping; no culture-sensitive conversion.

## 7. Enum mapping decisions

- `RuntimeExecutionResultStatus`: `Completed/Planned→success`,
  `NoExecutionRequired→no_change`, `Failed→failure`, `Cancelled→cancelled`,
  `PartiallyCompleted→failure` (Phase 32.1 rule: no new value introduced).
- `CycleCompletionStatus`: `Completed→success`, `PartiallyCompleted/Failed→
  failure`, `Cancelled→cancelled`.
- `DiagnosticSeverity`: `Pass/Info→pass`, `Warning→warning`, `Fail/Error→
  failure`. **Conservative deviation**: the enum has `Info` and `Error` not in
  the 32.1 catalog (`pass/warning/failure`); mapped to `pass`/`failure`
  respectively. Documented in tests.
- `CustomRouteDnsCacheState`: `Fresh→fresh`, `Stale→stale`, `Expired/Missing→
  miss`, `Failed/Disabled→failed`. **Conservative deviation**: `Expired` and
  `Missing` are not in the 32.1 `cache_state` set (`fresh/stale/miss/failed`);
  both collapse to `miss` (no usable cached value). Documented.
- `IranDirectCommand`: all 30 members map to their own member name via
  centralized `switch` (exhaustive; unknown→`unknown`).

## 8. Failure-category mapping

`OperationCanceledException→cancellation`, `TimeoutException→timeout`,
`HttpRequestException→http`, `IOException`/`UnauthorizedAccessException→io`,
`JsonException→serialization`. `FaultInjectionException` routes via
`FaultInjectionPoint`: `HttpRequest→http`, `DnsLookup→dns`,
`Route{Enumeration,Create,Delete}→routing`, `File{Read,Write,Move}→io`,
`Json{Load,Save}→serialization`, `NamedPipeSend→io` (documented choice: the
32.1 "io or invalid_response" fork resolved to `io` to avoid over-fitting IPC
transport failures to a response-shape category), `SnapshotCapture`/
`DiagnosticsRun→unknown`. Category never derived from exception text; inner
exceptions not traversed.

## 9. Tag validation design

`TelemetryTagValidator` is a static catalog (no reflection on the read path):
approved/prohibited membership sets, and a per-tag bounded-value `Dictionary`.
`IsValidBoundedValue` returns `false` for tags whose values are validated by
dedicated mappers (`operation`, `ipc_command`). Used by contract tests and
intended for future instrumentation helpers only.

## 10. Architecture isolation (evidence)

`TelemetryArchitectureTests` (assembly-scan, tests only) assert:
- no OpenTelemetry package references in `IranDirect.Core.csproj`;
- exactly one `ActivitySource` and one `Meter` definition in the assembly;
- no `StartActivity` outside the telemetry foundation;
- no `Meter.Create*` / `new Counter<>` / `new Histogram<>` / `new ObservableGauge<>`
  in production workflows;
- no generic free-form `StartActivity(name, …)` / `RecordMetric(name, …)` APIs;
- planner/execution/routing namespaces do not reference `Observability.Telemetry`;
- prohibited tag names appear only in the catalog definitions, never as applied
  tags (none exist yet).

## 11. Performance / allocation evidence

`TelemetryAllocationTests` (using `GC.GetAllocatedBytesForCurrentThread`):
- accessing `IranDirectTelemetry.ActivitySource` 1000× after warm-up:
  **0 bytes**.
- accessing `.Meter` 1000×: **0 bytes**.
- enum/failure mappers 1000×: **0 bytes** (exception constructed once before
  the loop, confirming the loop body is allocation-free).
- `IsApprovedTagName("outcome")` 1000×: **0 bytes**.

## 12. No-instrumentation / no-exporter status

Confirmed: zero `ActivitySource.StartActivity` calls in production workflows;
zero metric instruments created; `Program.cs` / `appsettings.json` unchanged;
no new package references; `IranDirectTelemetry` creates no listeners/exporters.

## 13. Tests added (90, all green)

`IranDirect.Core.Tests/Observability/Telemetry/`:
`IranDirectTelemetryTests` (6), `TelemetryNameCatalogTests` (4),
`TelemetryTagCatalogTests` (8), `TelemetryEnumMappingTests` (8),
`TelemetryFailureCategoryTests` (7), `TelemetryArchitectureTests` (7),
`TelemetryAllocationTests` (4). Coverage includes exact names, no duplicates,
no forbidden data patterns, approved/prohibited separation, bounded-value
acceptance/rejection, every enum member mapped, unknown fallbacks, no
`ToString` leakage, fault-injection matrix, allocation-free reads, and
architecture isolation.

## 14. Next phase

Phase 32.3 — Runtime-cycle tracing and metrics: attach spans/metrics at the
existing `RuntimePerfCategory` scope sites (one wrapper feeding both the
profiler and the `Meter`), implement the RuntimePerfReport integration decision
(32.1 §10 option A), no per-route spans.

## 15. Verification performed

- `dotnet build`: 0 warnings, 0 errors.
- Telemetry-focused suite: 90 passed.
- Full suite: green. Stress: green. Benchmark Release build: 0/0.
- No OpenTelemetry packages; no workflow instrumentation.
