# Phase 32.9 — OpenTelemetry Hosting and Export Configuration

## Goal

Add **optional** OpenTelemetry hosting for the telemetry already emitted by
`IranDirect.Core`. The Core telemetry foundation (ActivitySources, Meters, tag
catalog, privacy policy) from Phases 32.1–32.8 is unchanged. This phase only
adds a *hosting/export layer* inside `IranDirect.Service`, disabled by default.

It does **not** add new workflow instrumentation, does not change Activity
names / metric names / tag catalogs / telemetry semantics, does not change the
IPC wire format, and does not add telemetry to CLI or Tray business logic.

## Packages added (Service only)

Pinned to a non-vulnerable release (`1.15.3`):

- `OpenTelemetry.Extensions.Hosting` — `AddOpenTelemetry().WithTracing()/WithMetrics()`
- `OpenTelemetry.Exporter.OpenTelemetryProtocol` — optional OTLP export
- `OpenTelemetry.Exporter.Console` — optional development-only console export

No automatic instrumentation packages (HTTP, runtime, process, SQL, ASP.NET),
no vendor-specific exporters, no profiling agents. No OpenTelemetry package is
referenced by `IranDirect.Core`, CLI, Tray, Benchmarks, or Testing.

## Configuration contract

`IranDirect.Service/appsettings.json` (and `appsettings.Development.json`)
carry a disabled-by-default section. All values are safe to commit.

```json
"Observability": {
  "Enabled": false,
  "TracingEnabled": true,
  "MetricsEnabled": true,
  "SamplingRatio": 1.0,
  "ExportTimeoutSeconds": 5,
  "ShutdownFlushTimeoutSeconds": 5,
  "ServiceName": "IranDirect.Service",
  "Environment": "",
  "Otlp": {
    "Enabled": false,
    "Endpoint": "",
    "Protocol": "grpc",
    "HeadersEnvironmentVariable": ""
  },
  "Console": {
    "Enabled": false
  }
}
```

Conventions:

- **Telemetry disabled by default**; OTLP disabled by default; console exporter
  disabled by default.
- **Console exporter allowed only in the `Development` environment** and only
  when `Console.Enabled = true`. Otherwise validation fails startup.
- `SamplingRatio` is clamped to `0.0`–`1.0`.
- Timeout values are bounded (`1`–`60` seconds); `ShutdownFlushTimeoutSeconds`
  bounded to `1`–`30` seconds.
- No credentials are stored in `appsettings`. The OTLP header string is loaded
  from the environment variable named by `Otlp.HeadersEnvironmentVariable`.
- A blank `Otlp.Endpoint` never enables OTLP.

## Options model and validation

`IranDirect.Service/Observability/ObservabilityOptions.cs` defines the
narrowly scoped model:

- `ObservabilityOptions` (root)
- `ObservabilityOptions.OtlpExporterOptions` (nested `Otlp`)
- `ObservabilityOptions.ConsoleExporterOptions` (nested `Console`)

`ObservabilityOptionsValidator.Validate(options, isDevelopment = false)` is a
`static void` that throws a concise `ArgumentException` on the first problem and
**never includes secret values in the message**. It checks:

- `SamplingRatio` between `0.0` and `1.0`.
- `ExportTimeoutSeconds` / `ShutdownFlushTimeoutSeconds` within bounded ranges.
- OTLP `Endpoint` is an absolute `http(s)` URI when OTLP is enabled; blank
  endpoint is rejected when OTLP is enabled.
- Console exporter cannot be enabled outside `Development`.
- The header environment variable name is non-empty when set; if set, the
  variable must resolve to a non-empty value (a missing/empty header value
  fails configuration only when OTLP explicitly requests headers).

Validation runs in `Program.cs` via `AddIranDirectObservability` (which calls
`ObservabilityOptions.FromConfiguration`), so invalid configuration fails at
startup **before** any runtime work begins.

## Resource attributes (bounded)

`ObservabilityResourceBuilder.Create(serviceName, serviceVersion, environment)`
produces exactly:

- `service.name` — `ServiceName` (default `IranDirect.Service`).
- `service.version` — derived from `IranDirectTelemetry.Version` (the same
  stable source used by the Core telemetry contracts), not the config value.
- `deployment.environment.name` — `Environment` when non-empty, else the host
  environment name, else `"unknown"`.

`service.instance.id` is **not** generated (`autoGenerateServiceInstanceId:
false`) — the Phase 32.1 privacy policy requires explicit approval for
per-process identifiers and none was granted. No `machine.name`, `host.name`,
`user.name`, `process.command_line`, local IP, installation path, or
organization identifier is attached.

## Tracing registration

When `Enabled && TracingEnabled`:

- `AddOpenTelemetry().WithTracing(...)` adds **only** the Core ActivitySource
  (`IranDirectTelemetry.ActivitySource`, source name `IranDirect.Core`).
- Parent-based ratio sampling: `ParentBased(TraceIdRatioBased(SamplingRatio))`.
- OTLP exporter added only when `Otlp.Enabled`.
- Console exporter added only in `Development` and when `Console.Enabled`.

No automatic instrumentation packages; no activity processors that buffer
arbitrary spans in memory. The existing `ActivitySource` creation in Core is
untouched. When tracing is disabled, no `TracerProvider` is registered and the
Core `ActivitySource` behavior remains harmless (no listeners → no export).

## Metrics registration

When `Enabled && MetricsEnabled`:

- `AddOpenTelemetry().WithMetrics(...)` adds **only** the Core Meter
  (`IranDirectTelemetry.Meter`, meter name `IranDirect.Core`).
- OTLP exporter added only when `Otlp.Enabled`.
- Console exporter added only in `Development` and when `Console.Enabled`.

No runtime/process meters are added automatically. Existing Meter instruments
are unchanged. No duplicate meters/instruments are created in Service.

## OTLP configuration

- `Endpoint` — absolute `http(s)` URI; `Protocol` `grpc` (default) or
  `http/protobuf` (mapped to `OtlpExportProtocol`).
- `ExportTimeoutSeconds` → `OtlpExporterOptions.TimeoutMilliseconds`.
- `Headers` loaded from the environment variable named by
  `HeadersEnvironmentVariable`; the value never appears in configuration,
  logs, or exception messages.
- No custom HTTP client or retry loop. Bounded by OpenTelemetry defaults.

## Console exporter policy

- Allowed **only** in `Development` AND with `Console.Enabled = true`.
- Never enabled merely because OTLP is disabled.
- If enabled outside `Development`, options validation fails startup with a
  concise configuration error (no silent enable, no production payload print).

## Failure isolation

- Collector unavailable does not crash runtime workflows: export happens on the
  SDK's background pipeline; exporter errors are isolated by the SDK and logged,
  never propagated into Core business calls.
- Exporter queue/backpressure is bounded by OpenTelemetry defaults; the Windows
  Service stop cannot block indefinitely (providers are disposed by framework
  hosting).
- Invalid configuration fails during startup, before runtime work begins; one
  concise error is sufficient (no repeated error-log storm — validation runs
  once at registration).
- No Core telemetry call is wrapped in business-level try/catch.

Proven by `ObservabilityIntegrationTests.ExporterFailure_DoesNotFailBusinessOperation`
(a throwing exporter never propagates into an instrumented operation) and by
the disabled/no-exporter registration tests.

## Shutdown and flush

- Provider disposal occurs during host shutdown via `OpenTelemetry.Extensions.Hosting`
  (the same hosted-service lifetime as the rest of the worker).
- `ShutdownFlushTimeoutSeconds` documents the bounded flush window; framework
  disposal is sufficient (no custom infinite wait, no `ForceFlush` on every
  cycle/request).
- Telemetry shutdown failures do not prevent process termination.

## Composition-root isolation

All OpenTelemetry registration lives under
`IranDirect.Service/Observability/`:

- `ObservabilityOptions.cs`
- `ObservabilityOptionsValidator.cs`
- `ObservabilityResourceBuilder.cs`
- `ObservabilityServiceCollectionExtensions.cs`

`Program.cs` change is mechanical:

```csharp
builder.Services.AddIranDirectObservability(
    builder.Configuration,
    builder.Environment.EnvironmentName);
```

`IranDirect.Core` remains BCL-only (no `OpenTelemetry.*` namespaces). CLI and
Tray gain no OpenTelemetry package references. The Core telemetry contracts
(`IranDirectTelemetry`, activity/metric name constants, tag catalog) are
unchanged.

## Tests

`IranDirect.Service.Tests` (new project):

- `ObservabilityOptionsTests` — defaults disabled, valid/invalid OTLP, invalid
  sampling/timeout, console rejected outside Development / allowed in
  Development, missing header env var behavior, secret value not in messages.
- `ObservabilityResourceBuilderTests` — exact `service.name`, non-empty
  `service.version`, environment name, no forbidden attributes, no instance id.
- `ObservabilityRegistrationTests` — disabled registers no provider, tracing-
  only, metrics-only, both, approved no-exporter mode, invalid config fails at
  registration.
- `ObservabilityIntegrationTests` — existing `IranDirect.RuntimeCycle` activity
  exported, existing metric collected, disabled/no-source exports nothing,
  sampling ratio 0 → no spans / 1 → spans, metrics unaffected by trace
  sampling, exporter failure does not fail business op, no forbidden tags.

Architecture tests extended in `IranDirect.Core.Tests` (`TelemetryArchitectureTests`):

- OTel package references only in Service (+ Service.Tests).
- Core/CLI/Tray/Benchmarks/Testing remain OTel-free.
- No automatic instrumentation packages.
- No secrets in appsettings.
- No forbidden resource attributes.
- Exactly one Service registration extension.
- Existing Activity/metric names unchanged.

All existing telemetry listener tests, runtime-cycle/planning/execution
hierarchy, route/prefix/DNS/IPC/support telemetry, service startup, Windows
Service lifecycle, cancellation, stress, and CLI/Tray tests are preserved.
Disabled remains the default test behavior.

## Overhead

- No provider/exporter thread/queue is created when `Enabled = false`.
- Provider construction and export are gated behind `Enabled`; existing
  ActivitySource/Meter behavior is unchanged when no listener is attached.
- Allocations match the committed Phase 32.8 baseline when disabled.
- No change to planner benchmarks or RuntimeExecutor stress behavior.

## Non-goals

- No new business/workflow instrumentation.
- No change to Activity names, metric names, tag catalogs, or telemetry
  semantics.
- No IPC wire-format change.
- No telemetry in CLI/Tray business logic.
- No automatic runtime/process/HTTP instrumentation.
- No vendor-specific or custom exporters.
