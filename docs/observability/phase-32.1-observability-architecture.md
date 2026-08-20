# Phase 32.1 — Observability architecture and telemetry baseline

Analysis, measurement and documentation only. No production code, test,
benchmark, package, or configuration source was modified. The repository
currently contains **no** `ActivitySource`, `DiagnosticSource`, `EventSource`,
`Meter`, `Counter<T>`, `Histogram<T>`, `ObservableGauge`, or OpenTelemetry
reference (verified by repo-wide grep; zero matches). All observability today is
delivered through four independent, mostly diagnostic mechanisms documented
below.

## 0. Repository facts that shape the design

- Telemetry layer must live under the **existing** `IranDirect.Core.Observability`
  namespace — it already exists and hosts `RuntimeSnapshot` /
  `RuntimeSnapshotProvider` / `IRuntimeSnapshotProvider`. New telemetry
  contracts belong there or in a new sibling namespace under it.
- `RuntimeCycleProfiler` already provides ambient, `AsyncLocal`-flowing,
  no-op-fast-path, best-effort cycle measurement with a **bounded enum**
  `RuntimePerfCategory` (16 values) and a persisted `RuntimeCyclePerfReport`.
  This is the natural seed for the metrics contract and the
  RuntimePerfReport integration decision (§10).
- Every bounded tag proposed in §7 already has an authoritative enum in the
  codebase, so tags are enumerated, not free-form.
- Hosting is `Host.CreateApplicationBuilder` + `AddWindowsService`, with a
  `appsettings.json` that already carries a `Profiling:Enabled` switch and no
  secrets. Telemetry configuration slots into the same pattern.

## 1. Existing mechanism inventory

| Mechanism | File / type | Responsibility | Data captured | Correlation | Destination | Class | Overlap |
|---|---|---|---|---|---|---|---|
| Cycle profiler | `Runtime/Profiling/RuntimeCycleProfiler.cs` (+ `RuntimePerfReportStore`, `RuntimePerfAccumulator`) | Ambient per-cycle timing for enable/disable/repair flows | 16 `RuntimePerfCategory` durations (min/max/avg/p95/count), total ms, planned/completed steps, completion status, error summary | `AsyncLocal` async-flow context; none cross-process | Per-cycle JSON file via `RuntimePerfReportStore` | Operational telemetry (persisted) | None — sole timing source |
| Performance report | `Runtime/Profiling/RuntimeCyclePerfReport.cs`, `RuntimeCyclePerfReport.cs` | Final persisted artifact per cycle | trigger, started/completed, total ms, category summaries, status, error, step counts | same context as profiler | JSON file, also embedded in `RuntimeSnapshot` | Operational / diagnostic | Superset of duration data |
| Runtime snapshot | `Observability/RuntimeSnapshot.cs`, `…Provider.cs` | Point-in-time health + perf snapshot for IPC/support | config, status, operation, prefix metadata, DNS cache status list, **`Performance` perf report**, counts | requested via IPC command | In-memory, returned to IPC, embedded in support bundle | Diagnostic state | Re-surfaces perf report |
| Diagnostic report | `Diagnostics/DiagnosticRunner.cs`, `DiagnosticReport.cs` | Health assessment across checks | per-check results, severity, pass/warn/fail summary | none (not a span model) | returned to IPC / support bundle | Diagnostic state | Independent of perf |
| Support bundle | `Support/SupportBundleExporter.cs`, `SupportSnapshotExporter.cs` | Explicit diagnostic artifact export | full snapshot JSON + ZIP | none | atomic JSON file + ZIP on disk | Diagnostic state (explicit) | Aggregates snapshot |
| Structured logging | `ILogger` in 4 `IranDirectController` sites, `PrefixUpdateMonitor`, `NamedPipeCommandServer`, `IranDirectWorker` | Operator-facing operational log | 15 calls total (6 Info, 6 Warn, 3 Error); no Debug/Trace, no scopes | none today; no trace ID | configured logger sink | Operational logs | None structured |

Key findings:
- **Logging is sparse and unstructured for correlation**: 15 calls, no
  `BeginScope`, no `TraceId`/`SpanId`, no log levels below Information, no
  per-route detail. It is a safety-net, not a telemetry channel.
- **Timing already exists** but is persisted-per-cycle only; it is not exported,
  not aggregated, and not correlated across cycles.
- **No cancellation vs failure distinction in logs**: the profiler records
  `CycleCompletionStatus.Cancelled`, but the three `LogError` calls and the
  worker's error path do not distinguish cancellation from failure.
- **No high-cardinality leakage today** because nothing emits route-level
  telemetry; the risk is entirely in *future* instrumentation, which §4–§9
  constrain.

## 2. Major runtime workflow maps

### A. Runtime reconciliation cycle
- Entry: `IranDirectWorker.ExecuteAsync` timer loop →
  `IranDirectController.RunCycleAsync` / `EnableAsync` / `DisableAsync`
  (`BeginCycleIfNone("repair"|"enable"|"disable")`).
- Chain: observe (`IranDirectRuntimeObservationSource` — 4 measured categories)
  → desired-collect → build decision (`PlanningDecisionBuild`) → build preview
  → plan changes (`RuntimeChangeSetPlanner`) → execute
  (`WindowsRuntimeExecutionStepHandler` — 11 measured categories) → persist
  inventory (`PersistenceStateSave`, `PersistenceInventorySave`) → finalize
  report.
- Current measurements: the 16 `RuntimePerfCategory` scopes, plus worker-level
  `Stopwatch.GetTimestamp` around `ExecuteAsync` (no category).
- Outcome model: `RuntimeCycleExecutionResult` + `RuntimeExecutionResultStatus`
  (NoExecutionRequired, Planned, Completed, Failed, Cancelled,
  PartiallyCompleted). Maps cleanly to the §7 `outcome` tag.
- Cancellation: `stoppingToken` flows; nested cycles return no-op scope so the
  outer trigger stays authoritative.
- Sensitive fields: destination prefixes, gateways, interface indexes, VPN
  addresses present in route objects — must never become tags.

### B. Prefix update workflow
- Entry: `PrefixUpdateMonitor.StartAsync` timer / `ForceCheckAsync`; controller
  `UpdatePrefixesAsync`.
- Chain: schedule/force → HEAD/GET → compare → metadata/history update →
  failure recovery (tracks `ConsecutiveFailures`).
- Current measurements: none timed; `ConsecutiveFailures` counter is in-memory
  snapshot only.
- Outcome: success/failure; `ConsecutiveFailures` is a gauge candidate (§6).

### C. Custom-route DNS workflow
- Entry: `CustomRouteDnsCacheService` / `CustomRouteResolver`.
- Chain: cache read → fresh hit / stale refresh → DNS request → stale fallback
  → failure persistence (`CustomRouteDnsCacheStatus`, `LastError`).
- Current measurements: none timed; cache state enum
  `CustomRouteDnsCacheState` (Fresh, Stale, Expired, Failed, Missing, Disabled)
  is the **authoritative `cache_state` / `source` tag source**.
- Sensitive: `Domain`, resolved `IPv4Addresses` — prohibited as tags.

### D. IPC workflow
- Entry: `IranDirectServiceClient` → `NamedPipeClientConnection` →
  `NamedPipeCommandServer` dispatch → command handler → `ServiceResponse`.
- Chain: request create → pipe connect → send → server dispatch → handler exec
  → response → timeout/cancel/fail.
- Current measurements: none timed; 3 `LogError` in server.
- Outcome: per-command success/failure; `IranDirectCommand` enum (30 values)
  is the **authoritative bounded `ipc_command` tag source**.
- No request ID exists today; §8 recommends introducing one later if
  cross-call correlation is needed (not required for spans — the IPC span
  itself is the correlation unit).

### E. Support bundle workflow
- Entry: `SupportBundleCommandHandler` → `SupportBundleExporter.ExportAsync`.
- Chain: snapshot capture (`RuntimeSnapshotProvider`) → serialize JSON
  (`SupportSnapshotSerializer`) → atomic temp-write + rename → ZIP create →
  cleanup.
- Current measurements: none timed.
- Sensitive: bundle is the **only** sanctioned place for detailed route/identity
  data (§9). Must not become a continuous telemetry substitute.

## 3. Telemetry principles (IranDirect rules)

1. Telemetry must never alter runtime behavior. (Profiler already honors this
   via no-op scopes and best-effort writes.)
2. Telemetry failures must never fail route reconciliation.
3. No route destination, gateway, domain, pipe payload, file path, user name,
   machine name, or IP may be a metric/span **tag**.
4. Metrics use bounded-cardinality tags only (§7 enums).
5. Detailed identities appear only in structured logs at an explicitly approved
   level and are excluded by default.
6. Traces carry operation categories and counts, never full route lists.
7. Cancellation is distinguishable from failure (`outcome=cancelled`).
8. Benign race outcomes (e.g. best-effort inventory write lost) are not errors.
9. Expected no-op cycles (`NoExecutionRequired`) emit no warning/error
   telemetry.
10. Support bundles remain explicit diagnostic artifacts, not continuous
    telemetry.
11. `DiagnosticReport` stays a health assessment, not an OTel span model.
12. `RuntimePerfReport` stays a persisted artifact; integration decision in §10.
13. No telemetry object enters planner/executor business models.

## 4. ActivitySource contract

- **Source name**: `IranDirect` (constant). **Version**: `Assembly version` of
  `IranDirect.Core`, set via `ActivitySource` ctor; aligned with the existing
  `RuntimeSnapshot.SchemaVersion` philosophy.
- **Sampling**: default parent-based; root spans sampled at a low fixed ratio in
  production (e.g. 1:10) except `IranDirect.SupportBundleExport` which is
  always-on (rare, operator-triggered). No per-route spans ever.

### Root spans
| Span | Kind | Owner | Parent | Status | Sampled |
|---|---|---|---|---|---|
| `IranDirect.RuntimeCycle` | Internal | `IranDirectController.Enable/Disable/RunCycle` | none | success/failed/cancelled/no_change | yes (low ratio) |
| `IranDirect.PrefixUpdateCheck` | Internal | `PrefixUpdateMonitor` | none | success/failed | yes |
| `IranDirect.CustomRouteRefresh` | Internal | `CustomRouteDnsCacheService` | none | success/failed | yes (low) |
| `IranDirect.IpcRequest` | Server | `NamedPipeCommandServer` | none | success/failed/cancelled/timeout | yes |
| `IranDirect.SupportBundleExport` | Internal | `SupportBundleExporter` | none | success/failed | **always** |

### Child spans (only where they add value)
`RuntimeCycle` children: `Runtime.Observe`, `Runtime.BuildDecision`,
`Runtime.BuildPreview`, `Runtime.PlanChanges`, `Runtime.Execute`,
`Runtime.PersistInventory`. These map 1:1 to existing profiler categories, so
they are justified and cheap. `Routes.Enumerate/Create/Delete` are **NOT**
spans — they are bulk operations inside `Runtime.Execute`; emitting one span per
route would blow cardinality. Route-system-call duration is recorded as a
histogram (§6) at the operation-batch level, not as child spans.
`Prefix.HttpHead`, `Prefix.HttpGet`, `Prefix.Compare`, `Prefix.PersistMetadata`
— only `Prefix.HttpGet` (and `Head` if used) are spans; the rest are attributes
on the compare/persist steps measured by the existing profiler categories, not
span-worthy. `Dns.CacheRead`/`Dns.Resolve`/`Dns.CacheWrite` — `Dns.Resolve` is a
span (external dependency); cache read/write are batch-level histogram events.
`Ipc.Connect`/`Send`/`Dispatch`/`Receive` — `Ipc.Dispatch` is the span boundary
inside the server; connect/send are client-side and folded into the client
`IpcRequest` span. `Support.CaptureSnapshot`/`Serialize`/`WriteJson`/`CreateZip`
— `CaptureSnapshot` and `CreateZip` are spans (I/O boundaries); serialize/write
are in-span events.

### Common span rules
- Tags: `operation`, `outcome`, `trigger`, plus span-specific bounded enums.
  No free-form values.
- Events: exception recorded once via `Activity.SetStatus(ActivityStatusCode.Error)`
  + `AddException` at the **owning boundary**; child spans set their own status
  but do not re-record the same exception.
- `outcome=cancelled` when `OperationCanceledException`/token fired;
  `outcome=timeout` for explicit timeouts; `outcome=failure` otherwise.

## 5. Metrics catalog

Instrument: `Meter("IranDirect")`. All tag sets bounded by §7.

**Counters** (`runtime.cycles.started`, `…completed`, `…failed`, `…cancelled`;
`runtime.repair.changes`, `…no_changes`; `routes.ops.requested`,
`…succeeded`, `…failed`; `prefix.checks`, `prefix.check.outcomes`;
`dns.lookups`, `dns.cache_hits`, `dns.stale_fallbacks`, `dns.failures`;
`ipc.requests`, `ipc.outcomes`; `support.bundles.exported`, `…failed`).

**Histograms** (`runtime.cycle.duration`, `runtime.observe.duration`,
`runtime.plan.duration`, `runtime.execute.duration`,
`routes.systemcall.duration`, `prefix.check.duration`, `dns.lookup.duration`,
`ipc.request.duration`, `support.bundle.duration`,
`runtime.cycle.op_count`, `runtime.cycle.changed_routes`).

**Observable gauges** (stable current values only): `service.enabled`,
`runtime.worker.active`, `prefix.known_count`, `route.inventory_count`,
`dns.cache_count`, `prefix.consecutive_failures`.

**Forbidden tags on any instrument**: destination prefix, gateway, interface
index, domain, command text, output path, exception message, diagnostic ID,
execution-step identity, IP address.

**Relationship to RuntimePerfReport**: the histograms above (cycle/observe/
plan/execute durations) overlap the 16 profiler categories. Decision in §10.

## 6. Approved bounded tags

| Tag | Allowed values | Source enum | Fallback |
|---|---|---|---|
| `operation` | observe, build_decision, build_preview, plan_changes, execute, persist_inventory, prefix_check, dns_resolve, ipc_dispatch, support_export | `RuntimePerfCategory` (mapped) | `unknown` |
| `outcome` | success, failure, cancelled, timeout, no_change | `RuntimeExecutionResultStatus` / `CycleCompletionStatus` | `unknown` |
| `trigger` | scheduled, forced, cli, tray, startup, repair | controller entry | `unknown` |
| `route_kind` | prefix, endpoint | `RouteInventoryItem`/`DesiredPrefixRoute` kind | `unknown` |
| `change_kind` | create, delete | `RuntimeChangeKind` | `unknown` |
| `source` | official, custom, cache | `CustomRouteDnsCacheState` family | `unknown` |
| `cache_state` | fresh, stale, expired, failed, missing, disabled | `CustomRouteDnsCacheState` | `missing` |
| `ipc_command` | (30-value `IranDirectCommand` enum names) | `IranDirectCommand` | `unknown` |
| `diagnostic_severity` | pass, warning, failure | `DiagnosticSeverity` | `pass` |
| `service_state` | enabled, disabled | config | `disabled` |
| `failure_category` | io, timeout, cancellation, http, dns, routing, serialization, invalid_response, unknown | new enum (implementation phase) | `unknown` |

No free-form tag values permitted; every value must be an enum member.

## 7. Logging-correlation policy

- `Activity.TraceId`/`SpanId` should be injected automatically by the
  configured logger formatter (implementation phase 32.6), so existing
  `LogInformation/Warning/Error` calls gain correlation with zero code change.
- `BeginScope` remains useful only for cross-cutting ambient values (e.g.
  `trigger`) where a span is not yet open; otherwise scope is redundant with the
  Activity.
- Runtime-cycle IDs: **not** introduced — `Activity.TraceId` is sufficient; a
  separate ID would duplicate state.
- IPC request IDs: optional later; not required because the `IranDirect.IpcRequest`
  span is the correlation unit.
- Exception logging (no-duplicate): log the exception **once** at the boundary
  that owns recovery (controller / server handler). Child operations set span
  status but do not log the same exception again. Expected converted failures
  (e.g. benign race, no-op cycle) use structured `outcome` logs, not stack
  traces.
- No-op cycles: logged at most at Information with `outcome=no_change`; never
  Warning/Error.

## 8. Privacy and cardinality matrix

| Field | Metric tag | Span tag | Debug log | Support bundle | Verdict |
|---|---|---|---|---|---|
| Destination prefix | ❌ | ❌ | ❌ | ✅ | prohibited |
| Gateway / next-hop | ❌ | ❌ | ❌ | ✅ | prohibited |
| Interface index/name | ❌ | ❌ | ❌ | ✅ | prohibited |
| VPN endpoint address | ❌ | ❌ | ❌ | ✅ | prohibited |
| DNS domain | ❌ | ❌ | ❌ | ✅ | prohibited |
| DNS resolved IPs | ❌ | ❌ | ❌ | ✅ | prohibited |
| HTTP URL | ❌ | ❌ (host only, bounded) | ❌ | ✅ | prohibited except bounded host |
| Named-pipe payload | ❌ | ❌ | ❌ | ✅ | prohibited |
| Local file path (output) | ❌ | ❌ | ❌ | ✅ (path only) | prohibited as tag |
| Machine/user name | ❌ | ❌ | ❌ | ⚠️ (machine only) | prohibited |
| Exception message | ❌ | ❌ | ✅ (boundary only) | ✅ | prohibited as tag |
| Operation category | ✅ | ✅ | ✅ | ✅ | allowed (bounded) |
| Outcome/trigger/source | ✅ | ✅ | ✅ | ✅ | allowed (bounded) |

Redaction rule: any attribute reaching a tag dimension must pass through the
§7 enum mapping; anything not in an enum is dropped or hashed to a fixed bucket
at the telemetry boundary, never passed raw.

## 9. RuntimePerfReport integration decision

**Chosen: (A) keep `RuntimePerfReport` independent; instrument the same
boundaries mechanically.**

Rationale:
- The profiler already produces per-cycle JSON with p95/count and writes it
  best-effort. Replacing it with OTel callbacks (option C) would couple a
  persisted on-disk diagnostic artifact to exporter availability, violating
  principle 2 and the support-bundle determinism contract.
- Deriving the report from telemetry (C) or sharing a measurement result (B)
  both add coupling and a failure mode where a missing collector loses the
  persisted report. Option A keeps the report as a local-first artifact and
  lets the metrics histograms (§6) *mechanically mirror* the same 16 categories
  — same start/stop calls, different sink.
- To prevent double-timing and semantic drift: the implementation phase should
  add the `Meter` recordings at the **same** `RuntimePerfCategory` scope sites
  the profiler already uses (one wrapper), so a single `Measure` call feeds both
  sinks. The profiler remains the source of truth for the persisted file; the
  meter is a secondary, non-blocking fan-out. No shared mutable measurement
  object is required.

## 10. Diagnostic and support-bundle integration

Telemetry metadata may appear in the snapshot/bundle **only as aggregate
summaries**, never as spans retained in memory:
- `RuntimeSnapshot`: may later carry a small `TelemetryStatus` block
  (exporter connected?, dropped-count, config enabled) — added in a later
  phase, not this one.
- `DiagnosticReport` / `SupportSnapshot`: unchanged; they are diagnostic state,
  not telemetry sinks.
- A future support bundle **may** include: current trace ID of the export
  operation, a recent in-memory metric snapshot (counts only), telemetry config
  state, exporter connectivity status, dropped-telemetry count.
- Rejected additions: any field exposing sensitive data (§8), duplicating
  existing diagnostics, requiring arbitrary span retention in memory, or
  altering bundle determinism. The bundle must remain byte-deterministic for
  identical runtime state.

## 11. Configuration and exporter design

Future `appsettings.json` (no secrets; follows existing `Profiling:Enabled`):
```json
{
  "Telemetry": {
    "Enabled": false,
    "TracingEnabled": false,
    "MetricsEnabled": false,
    "OtlpEndpoint": "",
    "SamplingRatio": 0.1,
    "ConsoleExporterAllowed": false,
    "ServiceName": "IranDirect",
    "ServiceVersion": "",
    "Environment": ""
  }
}
```
- Settings source: `appsettings.json` primary, environment variables
  (`IRANDIRECT_TELEMETRY__*` eventually) override, CLI never required.
- Auth/headers: OTLP headers from environment only, never appsettings.
- `ConsoleExporterAllowed` true only in Development (existing
  `appsettings.Development.json` pattern).
- **Disabled by default** in all phases until 32.6; a disabled meter/source is
  a no-op fast path (mirrors profiler `Noop`).
- No packages or config files added in this phase.

## 12. Failure and shutdown behavior

- Exporter unavailable / collector timeout / queue full: telemetry is dropped
  after a bounded queue; one concise infrastructure warning logged, never an
  exception propagated to the runtime cycle.
- Process shutdown / Windows Service stop: bounded flush (e.g. ≤2 s) then
  discard; shutdown must not block `OnStopping`.
- Worker cancellation: spans are marked `outcome=cancelled`; in-flight exports
  abandoned after flush bound.
- Unhandled exception: reported via existing boundary log + span error status;
  does not prevent service startup or next cycle.
- Offline operation: telemetry is fire-and-forget; no retry storm, no disk
  growth beyond bounded queue.

## 13. Test strategy (for implementation phases)

- **Unit**: span names/tags/status; metric names/tags/values; bounded-tag
  validation (rejects free-form); cancellation vs failure mapping; no sensitive
  tags; no span-per-route (assert child span count is constant w.r.t. route
  count); `ActivitySource` no-listener fast path (zero allocation when no
  listener); metric listener capture; no behavior change when telemetry
  disabled (planner benchmarks unchanged).
- **Integration**: runtime-cycle trace hierarchy; prefix-update trace;
  DNS cache outcomes; IPC correlation; exporter-failure isolation; service
  shutdown flush bounds.
- **Architecture**: telemetry abstractions confined to `Observability` layer /
  composition root; planner models telemetry-free; no high-cardinality tags; no
  telemetry package references in Core model namespaces.
- **Performance**: disabled-listener overhead; enabled in-memory-listener
  overhead; allocation per no-op cycle; no regression to existing planner
  benchmarks (Phase 30.2/30.3 baselines remain authoritative).

## 14. Implementation roadmap

- **32.2 — Core telemetry contracts**: `ActivitySource`/`Meter` definitions,
  shared names/tags/outcome helpers, no workflow instrumentation yet;
  architecture + contract tests. *Files:* new `Observability/Telemetry/*`.
  *Risks:* namespace pollution. *Gates:* 0 warnings, contract tests green,
  no-listener fast path proven. *Commit:* `feat(telemetry): add core activity and meter contracts`
- **32.3 — Runtime-cycle tracing + metrics**: observe/plan/execute spans;
  RuntimePerfReport integration (option A) via shared `Measure` wrapper; no
  per-route spans. *Files:* controller, profiler, executor. *Gates:* trace
  hierarchy test, no double-timing, planner benchmarks unchanged.
  *Commit:* `feat(telemetry): instrument runtime cycle tracing and metrics`
- **32.4 — Prefix/DNS telemetry**: HTTP + DNS/cache outcomes; privacy/card
  tests. *Files:* `PrefixUpdateMonitor`, `CustomRouteDnsCacheService`.
  *Commit:* `feat(telemetry): instrument prefix and dns workflows`
- **32.5 — IPC + support-export telemetry**: request correlation span; support
  export metrics/traces; failure isolation. *Files:* `NamedPipeCommandServer`,
  `SupportBundleExporter`. *Commit:* `feat(telemetry): instrument ipc and support export`
- **32.6 — OpenTelemetry hosting/export**: OTLP packages + DI; disabled by
  default; shutdown/flush. *Files:* `Program.cs`, `appsettings*`.
  *Commit:* `feat(telemetry): add opentelemetry export and hosting`
- **32.7 — Observability validation**: full integration suite; overhead
  benchmarks; runbook. *Commit:* `test(telemetry): validate observability suite and overhead`

## 15. Non-goals (explicit)

- No OpenTelemetry packages added in this phase (or until 32.6).
- No modification of `DiagnosticReport`, `RuntimeSnapshot`, support models.
- No per-route, per-prefix, per-DNS-address, or per-execution-step spans.
- No sensitive-field telemetry.
- No change to existing log statements.
- No runtime-behavior change from instrumentation.

## 16. Verification performed (this phase)

- Repo-wide grep: zero `ActivitySource`/`Meter`/OTel references.
- Build: 0 warnings, 0 errors (Release benchmark build 0/0).
- Focused observability suite: all `RuntimePerf*` / `RuntimeSnapshot` /
  `Diagnostic*` / `SupportSnapshot*` tests green.
- Full suite green; stress green; no `.cs`/`.csproj` modified.
