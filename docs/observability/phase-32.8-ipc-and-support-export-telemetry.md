# Phase 32.8 — IPC and Support Export Telemetry

Instrument the two remaining user-visible workflows:

- **Named-pipe IPC request/response** (`IranDirectServiceClient` ↔
  `NamedPipeCommandServer`): one client root Activity per request, three
  transport child Activities, one request counter, one request-duration
  histogram; plus one independent server-dispatch Activity per handled request.
- **Support snapshot and support bundle export** (`SupportSnapshotExporter`,
  `SupportBundleExporter`): one shared root Activity per user-visible export,
  bounded child Activities for capture/serialize/write/zip, one exported
  counter, one failed counter, one duration histogram.

This phase follows the Phase 32.1 architecture and the Phase 32.2 contracts:
no OpenTelemetry packages, no exporters, no hosting/appsettings changes, no
per-handler/per-entry spans, bounded tags only, no payload/path/identity
attachments.

## Approved Activity names

All names already existed in `IranDirectActivityNames` (no new name constants
were introduced):

| Name | Constant | Kind | Owner |
| --- | --- | --- | --- |
| `IranDirect.IpcRequest` | `IpcRequest` | Client root | `IranDirectServiceClient.SendAsync` |
| `Ipc.Connect` | `IpcConnect` | Client child | `IranDirectServiceClient` |
| `Ipc.Send` | `IpcSend` | Client child | `IranDirectServiceClient` |
| `Ipc.Receive` | `IpcReceive` | Client child | `IranDirectServiceClient` |
| `Ipc.Dispatch` | `IpcDispatch` | Server root | `NamedPipeCommandServer` |
| `IranDirect.SupportBundleExport` | `SupportBundleExport` | Shared root | `SupportSnapshotExporter` / `SupportBundleExporter` |
| `Support.CaptureSnapshot` | `SupportCaptureSnapshot` | Child | `SupportSnapshotExporter` |
| `Support.Serialize` | `SupportSerialize` | Child | `SupportSnapshotExporter` |
| `Support.WriteJson` | `SupportWriteJson` | Child | `SupportSnapshotExporter` |
| `Support.CreateZip` | `SupportCreateZip` | Child | `SupportBundleExporter` |

The JSON snapshot export and the ZIP bundle export share the single root name
`IranDirect.SupportBundleExport`, distinguished by the bounded `operation` tag
(`support_snapshot_export` vs `support_bundle_export`).

## Approved metrics

Defined in `IranDirectMetricNames` (no new metric name constants):

| Meter name | Instrument | Constant |
| --- | --- | --- |
| `irandirect.ipc.requests` | Counter<long> | `IpcRequests` |
| `irandirect.ipc.request.duration` | Histogram<double> | `IpcRequestDuration` |
| `irandirect.support.bundles.exported` | Counter<long> | `SupportBundlesExported` |
| `irandirect.support.bundles.failed` | Counter<long> | `SupportBundlesFailed` |
| `irandirect.support.bundle.duration` | Histogram<double> | `SupportBundleDuration` |

Exactly one `requests` increment and one `request.duration` sample per IPC
request attempt. Exactly one `bundles.exported` (or `bundles.failed`) increment
and one `bundle.duration` sample per support export (JSON or bundle).

## Bounded tags

`operation`, `ipc_command` (client only), `outcome`, `failure_category` (on
failure/timeout where the committed mapper returns a category).

New `operation` values added to `IranDirectTagValues`:
`OperationIpcRequest`, `OperationIpcConnect`, `OperationIpcSend`,
`OperationIpcReceive`, `OperationIpcDispatch`, `OperationSupportSnapshotExport`,
`OperationSupportBundleExport`, `OperationSupportCaptureSnapshot`,
`OperationSupportSerialize`, `OperationSupportWriteJson`,
`OperationSupportCreateZip`.

## Failure-category contract (pinned, not hardcoded)

`IpcRequestTelemetry` and `SupportExportTelemetry` never invent a
`failure_category`. They call `TelemetryFailureCategoryMapper.Map(exception)`
and emit the returned category string. The committed mapper produces:

- `NamedPipeSend` → `io` (this is what the mapper returns; the test asserts the
  mapper result rather than the literal `io`).
- `NamedPipeConnect` / `NamedPipeRead` → no dedicated case → `unknown`.
- `IOException` → `io`; `TimeoutException` → `timeout` (with **no**
  `failure_category` tag, because the mapper returns `category: null` for
  timeout); `OperationCanceledException` → `cancelled` (no `failure_category`,
  status left `Unset`); `JsonException` → `serialization`; a JSON literal
  `null` response → `invalid_response` (via `ServiceResponse.ErrorCode ==
  INVALID_RESPONSE`).

A **failed business `ServiceResponse`** (e.g. `ErrorCode == "INVALID_BUNDLE_PATH"`)
is not a transport fault: `Ipc.Connect`, `Ipc.Send`, and `Ipc.Receive` all
complete `success`, the root is `failure` with **no** `failure_category` tag.

## Transport child-attempt semantics (verified)

The three client children are created only around the operations that actually
execute, in order, inside one `using` scope each:

- send fault raised before connect → no `Ipc.Connect`/`Ipc.Send`/`Ipc.Receive`
  children; root fails (category via mapper); `factory.ConnectCallCount == 0`.
- connect throws → `Ipc.Connect` completes (timeout→`timeout` no category, or
  other→`unknown`/`io` per mapper); no `Ipc.Send`/`Ipc.Receive`; root fails.
- write failure after connect → `Ipc.Connect` success, `Ipc.Send` fails, no
  `Ipc.Receive`; root fails.
- read/parse failure → `Ipc.Connect`+`Ipc.Send` success, `Ipc.Receive` fails;
  root fails (malformed JSON → `serialization`; literal `null` →
  `invalid_response`).

A pre-cancelled call: the connect child is still **attempted once** (connect
count == 1; the client serializes the request before cancellation is observed,
so serialization is NOT zero), the connect child completes `cancelled`, the
root is `cancelled` with status `Unset` and no `failure_category`.

## No duplicate roots for bundle export (explicit nested path, no Activity.Current)

`SupportBundleExporter` drives the snapshot exporter through the explicit
internal-ish method `ISupportSnapshotExporter.ExportWithinBundleAsync`, which
routes to `SupportSnapshotExporter.ExportCoreAsync(..., createRootTelemetry:
false)`. That path calls `SupportExportTelemetry.StartNested` — it creates no
second `IranDirect.SupportBundleExport` root and records no terminal metric;
its `CaptureSnapshot`/`Serialize`/`WriteJson` children attach to the enclosing
bundle root. The decision is **explicit** (the caller chooses the nested
method) and is **never** derived from `Activity.Current`, so it is immune to
sampling, disabled listeners, an unrelated ambient Activity, or future
refactoring of Activity lifetime. `ExportWithinBundleAsync` has a default
interface implementation forwarding to `ExportAsync`; only
`SupportSnapshotExporter` overrides it with the non-root path. The test
`BundleExport_SingleRootNoDuplicate_ChildrenAttachedToBundle` (and the new
structural tests below) assert exactly one root, each child parented to that
single root, one `bundles.exported`, one `bundle.duration`, no duplicate.

## Privacy

No pipe name, request/response payload, serialized JSON, command arguments,
route identities, output path, filename, temporary path, ZIP entry name,
payload size, support contents, machine name, or exception message is attached.
The contract is enforced by `TelemetryTagValidator` plus the architecture test
`ProhibitedTagConstants_DoNotAppearInProductionSource` and the
`*_OnlyApprovedTags_NoProhibited` tests.

## Server/client independence

The named-pipe server runs in `IranDirect.Service` (a separate process).
Activity context cannot cross the named pipe without changing the wire
protocol, which is explicitly forbidden this phase. The client
`IranDirect.IpcRequest` root and the server `Ipc.Dispatch` root are therefore
intentionally independent (not parented). `IpcDispatchTelemetry.Start` is `public`
so the `IranDirect.Service` assembly can call it.

## Files

- `IranDirect.Core/Observability/Telemetry/IpcRequestTelemetry.cs` (NEW)
- `IranDirect.Core/Observability/Telemetry/IpcDispatchTelemetry.cs` (NEW)
- `IranDirect.Core/Observability/Telemetry/SupportExportTelemetry.cs` (NEW)
- `IranDirect.Core/Observability/Telemetry/IranDirectTagValues.cs` (operation
  constants added)
- `IranDirect.Core/Ipc/IranDirectServiceClient.cs` (wired)
- `IranDirect.Core/Support/SupportSnapshotExporter.cs` (wired)
- `IranDirect.Core/Support/SupportBundleExporter.cs` (wired)
- `IranDirect.Service/Ipc/NamedPipeCommandServer.cs` (wired)

## Tests

- `IpcRequestTelemetryTests.cs` (NEW) — success tree, failed business response
  (transport children succeed, root fails, no free-form tags), send-fault-
  before-connect, connect timeout (outcome `timeout`, no category — pinned to
  the mapper), write-failure, read/parse failure (malformed JSON →
  `serialization`; `null` → `invalid_response`), pre-cancelled (connect
  attempted once, root `cancelled`, no category), and mapper-pinned failure
  category.
- `SupportExportTelemetryTests.cs` (NEW) — snapshot success (provider once,
  serializer once, write once, one exported, one duration, no payload-sized
  tags), bundle single-root no-duplicate (children attached to the one bundle
  root), bundle zip creation once, inner-failure (deterministic `IOException` →
  `io`, no export), the deterministic "file-where-directory-expected" I/O
  failure (`failure_category == io`, no permission dependence), and five
  structural tests proving the explicit nested path: a standalone snapshot
  export still creates its own root inside an unrelated ambient Activity; a
  bundle export creates exactly one root; a bundle export still creates exactly
  one root with no `ActivityListener` registered; export behavior and invocation
  counts are identical across `AllData`, `PropagationData`, and `None` sampling;
  and no exported/failed counter is duplicated.
- `TelemetryArchitectureTests.cs` — extended with IPC and Support export
  boundary tests (exactly-one root, approved constants, exact instrument
  counts, helper-only owners, server dispatch location).

All Phase 32.8 telemetry tests live in the existing non-parallel
`RuntimeCycleTelemetry` collection (shared `ActivitySource`/`Meter` listeners).

## Verification

- `dotnet build IranDirect.Core` / `IranDirect.Service` (Debug): succeeded,
  0 warnings.
- `dotnet build IranDirect.Benchmarks` (Release): succeeded.
- Full `IranDirect.Core.Tests`: passed (2150).
- Telemetry collection (`*Observability*`): 258 passed.
- Architecture + catalog + enum-mapping: passed.
- No `OpenTelemetry` package/exporter references in production.
