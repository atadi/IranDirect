# Phase 32.6 — Route System-Call Telemetry

Instruments the native Windows route-operation boundary — route enumeration,
creation, and deletion — with one Activity per native system call, an approved
set of route-operation counters, and one system-call duration histogram. This is
the fourth telemetry slice and the first to bridge into the native process
boundary (`powershell.exe` for enumeration, `netsh.exe` for create/delete).

## Hierarchy

During runtime execution the route spans nest under `Runtime.Execute`:

```text
IranDirect.RuntimeCycle            (root, Phase 32.3)
└── Runtime.Execute                (child, Phase 32.5)
    ├── Routes.Enumerate          (child, Phase 32.6)
    ├── Routes.Create             (child, Phase 32.6)
    └── Routes.Delete             (child, Phase 32.6)
```

`Routes.Enumerate` / `Routes.Create` / `Routes.Delete` are **children of
`Runtime.Execute`** (when invoked inside an instrumented execution) and share
the cycle `TraceId`. They are **not** children of `Runtime.PlanChanges` (planning
is a sibling of execution, never an ancestor of route spans). A route Activity
may become a root if the route API is used outside a runtime cycle — no fake
parent is manufactured.

The parent-child relationship is proven by
`RouteSystemCallTelemetryTests.RouteSpans_AreChildrenOfRuntimeExecute_NotPlanChanges`,
which reproduces the exact production span names (`IranDirect.RuntimeCycle` →
`Runtime.Execute` → `Routes.*`) with the real `WindowsRouteManager` composed with
the `TelemetryRouteApi` decorator, and asserts: each route span's
`ParentSpanId` equals `Runtime.Execute.SpanId`, all share the cycle `TraceId`,
none is a child of planning, and route spans stop before `Runtime.Execute`
stops.

## Native boundary selected

The narrow native boundary is `WindowsRouteApi` — each public method is exactly
one process submission:

- `EnumerateAsync` → one `powershell.exe` call (30 s timeout).
- `AddAsync(IReadOnlyCollection<ManagedRoute>)` → one `netsh.exe` batch (5 min
  timeout). One native call **per batch**, regardless of route count.
- `DeleteAsync(IReadOnlyCollection<ManagedRoute>)` → one `netsh.exe` batch.

Each method is the narrowest seam representing one actual native system call.

`WindowsRouteManager` (fault injection, the empty-batch guard, and orchestration)
calls `WindowsRouteApi` but is **not** instrumented — the architecture test
`PlannerAndModelNamespaces_DoNotReferenceTelemetry` forbids any telemetry
reference in `IranDirect.Core/Routing/`. Therefore a narrow decorator,
`TelemetryRouteApi` (in `Observability/Telemetry/`), wraps `IWindowsRouteApi` and
is the single bridge. `WindowsRouteApi` and the route models (`ManagedRoute`,
`SystemRoute`) stay telemetry-free.

The wrapper is registered at the composition root in `Program.cs` (a legitimate
interface seam, identical in spirit to the Phase 32.4 `IRuntimeChangeSetPlanner`
registration):

```csharp
builder.Services.AddSingleton<IRouteManager>(serviceProvider =>
{
    var commandRunner = serviceProvider.GetRequiredService<CommandRunner>();
    var windowsApi = new WindowsRouteApi(commandRunner);
    var telemetryApi = new TelemetryRouteApi(windowsApi);
    return new WindowsRouteManager(commandRunner, telemetryApi);
});
```

`RuntimeExecutor` and the `RuntimeExecutionStep` handler remain **telemetry-free**.

## Activity contract

| Span name | Constant | Kind | Emitted by |
| --- | --- | --- | --- |
| `Routes.Enumerate` | `IranDirectActivityNames.RoutesEnumerate` | `Internal` | `TelemetryRouteApi.EnumerateAsync` |
| `Routes.Create` | `IranDirectActivityNames.RoutesCreate` | `Internal` | `TelemetryRouteApi.AddAsync` |
| `Routes.Delete` | `IranDirectActivityNames.RoutesDelete` | `Internal` | `TelemetryRouteApi.DeleteAsync` |

All three are created from a single shared helper
(`RouteSystemCallTelemetry.Start`) using the approved name constants. No child
spans beneath any `Routes.*` span. The span **name never contains** a route
count or identity.

## Metric names / types / units

| Metric | Type | Unit | Description |
| --- | --- | --- | --- |
| `irandirect.routes.operations.requested` | `Counter<long>` | `{operation}` | Native route system calls requested. |
| `irandirect.routes.operations.succeeded` | `Counter<long>` | `{operation}` | Native route system calls that succeeded / changed nothing. |
| `irandirect.routes.operations.failed` | `Counter<long>` | `{operation}` | Native route system calls that failed / timed out. |
| `irandirect.routes.system_call.duration` | `Histogram<double>` | `ms` | Elapsed time of one native route system call. |

All four created exactly once against `IranDirectTelemetry.Meter`. No
enumeration/create/delete-specific counters, no per-route metrics, no
batch-size tag or metric.

## Operation / change-kind / route-kind tags

- `operation` ∈ { `enumerate_routes`, `create_routes`, `delete_routes` } (added
  to the bounded operation catalog in `IranDirectTagValues`).
- `change_kind` ∈ { `create`, `delete` } — present on `Routes.Create` /
  `Routes.Delete`, omitted on `Routes.Enumerate`.
- `route_kind` ∈ { `prefix`, `endpoint`, `unknown` }.

**Route-kind finding:** the native `WindowsRouteApi` boundary receives
`ManagedRoute` records that carry **no kind discriminator**, so the entire-batch
kind cannot be authoritatively determined here. Per the Phase 32.6 contract we
emit `route_kind = unknown` (a bounded value) rather than inspecting individual
routes to infer one. If `ManagedRoute` ever gains a kind discriminator this
helper (`TelemetryRouteApi.KindOf`) would read it. The `unknown` value is in the
approved bounded set.

## Outcome rules

Mapped centrally via `TelemetryFailureCategoryMapper` / bounded values:

- `success` — the native call completes successfully.
- `no_change` — reserved for an authoritative native "nothing changed" result.
  The current `WindowsRouteApi` contract has no such signal (empty batches are
  handled separately, below), so `no_change` is not emitted in practice. The
  `CompleteNoChange` terminal method exists for completeness.
- `failure` — all other native-call failures.
- `timeout` — `TimeoutException` or the existing 30 s / 5 min command timeout
  path (surfaced as `TimeoutException` by `CommandRunner`).
- `cancelled` — `OperationCanceledException` attributable to caller
  cancellation.

Outcomes are never derived from free-form command output.

## Failure-category policy

Set **only** when a concrete exception mapping exists (via
`TelemetryFailureCategoryMapper`):

- `IOException` / `UnauthorizedAccessException` → `io`
- `TimeoutException` → `timeout`
- `OperationCanceledException` → `cancellation` (used only on the real
  cancellation path; the `cancelled` outcome omits the category)
- `FaultInjectionException(RouteEnumeration / RouteCreate / RouteDelete)` →
  `routing`
- native command/process failures (`InvalidOperationException` from
  `CommandRunner` result inspection) → `routing` unless a more accurate typed
  mapping applies
- unknown → `unknown`

Exception `.Message`, process output, command line, script contents, and file
path are **never** attached to spans or metrics.

## Metric recording rules

For each actual native system-call attempt:

- **At start:** `requested += 1` (tagged with `operation`, and `change_kind` /
  `route_kind` where applicable — never `outcome` or `failure_category`).
- **On success / no-change:** `succeeded += 1`.
- **On failure / timeout:** `failed += 1`.
- **On cancellation:** `requested` remains; `failed` is **not** incremented
  (there is no approved route-cancelled counter).
- **Duration:** recorded exactly once for every started native call, including
  script/process setup, execution, output parsing, and command-result
  validation. Planner/executor orchestration time is excluded.

Counters and histogram never receive a per-route tag; the raw batch size is
never a tag or metric. Exactly-once terminal completion is enforced with an
`Interlocked` guard.

## Duration design

`Stopwatch.GetTimestamp` / `Stopwatch.GetElapsedTime` (the route boundary has
no injected `TimeProvider`). Milliseconds as `double`. Duration includes the
complete owned native operation. Double-timing is avoided: the single
`Start`/scope in `RouteSystemCallTelemetry` owns the measurement; neither
`WindowsRouteManager` nor `WindowsRouteApi` records any metric.

## Empty-batch behavior

`WindowsRouteApi.AddAsync` / `DeleteAsync` already return before native I/O when
the collection is empty. `TelemetryRouteApi` mirrors that guard: it checks
`routes.Count == 0` **before** starting the telemetry scope, so an empty batch
emits **no** `Routes.Create` / `Routes.Delete` Activity, **no** `requested` /
`succeeded` / `failed` increment, and **no** duration measurement. Native
behavior is preserved exactly (no move of the existing empty-batch check).
Explicit tests: `Create_EmptyBatch_EmitsNothingAndNoNativeCall`,
`Delete_EmptyBatch_EmitsNothingAndNoNativeCall`.

## Batch semantics

One native batch submission = one Activity + one `requested` + one terminal
counter + one duration. A 1,000-route batch (and a 50,000-route batch, proven by
`FiftyThousandRouteBatch_ProducesOneTelemetryScopeNotRouteProportional`) produces
exactly one `Routes.Create` Activity and one set of measurements — never
route-proportional telemetry allocation.

## Parent-child correlation evidence

`RouteSpans_AreChildrenOfRuntimeExecute_NotPlanChanges` drives the real
`WindowsRouteManager` + `TelemetryRouteApi` under a manually-started
`IranDirect.RuntimeCycle` → `Runtime.Execute` pair (exact production span names)
and verifies: 3 route spans, each a direct child of `Runtime.Execute`, sharing
the cycle `TraceId`, none a child of `Runtime.PlanChanges`, all stopping before
`Runtime.Execute` stops, and no extra child spans beyond the approved set.

## No-listener / allocation evidence

`NoListener_NativeBehaviorUnchangedAndNoException` runs `AddRoutesAsync` /
`DeleteRoutesAsync` / `GetIpv4RoutesAsync` with **no** `ActivityListener` and
**no** `MeterListener`: native behavior is identical, no exception is thrown,
and `StartActivity` returning `null` is a safe fast path. The 50K-batch test
proves telemetry allocation does not scale with batch size (one scope per
batch, not per route).

## Failure propagation

The decorator preserves the exact current contract (proven by the fault /
exception tests and by reusing the unchanged `WindowsRouteManager`):

- Faults thrown by `WindowsRouteManager` **before** native submission (the
  `FaultInjectionPolicy` / `ShouldFailAt` path) result in **zero** native calls
  and therefore **zero** telemetry — the wrapper is never reached
  (`*_RouteCreateFault` / `*_RouteEnumerationFault` / `*_RouteDeleteFault`).
- Native exceptions propagate **unchanged**; the decorator catches only to
  complete telemetry, then rethrows the original exception.
- Compensation / ownership / inventory behavior is untouched because the
  decorator adds no orchestration.
- A failed batch is never partially submitted (the single `netsh` batch is
  all-or-nothing at the process boundary).
- Cancellation remains cancellation; timeout remains timeout.

## Privacy / cardinality compliance

Only approved tags are emitted: `operation`, `outcome`, `change_kind`
(create/delete), `route_kind` (unknown), `failure_category` (failure/timeout
only). Prohibited names (`destination_prefix`, `gateway`, `next_hop`,
`interface_index`, `route_identity`, `command`, `exception_message`, `file_path`,
…) never appear. The batch size is never a tag or metric. No native command
text, output, exit code, or script content is placed on telemetry.

## Architecture isolation

- Exactly **one** production `StartActivity` location references the route
  constants (`RouteSystemCallTelemetry.cs`, using the approved
  `IranDirectActivityNames.Routes*` constants) — proven by the
  `ExactlyOneRouteSystemCallActivityLocation_NoPerRouteSpans` architecture test.
- Exactly **three** route counters (`requested`, `succeeded`, `failed`) and
  **one** route duration histogram; no per-route `StartActivity` / `Record` /
  `Counter.Add`.
- `WindowsRouteApi`, `WindowsRouteManager`, and the route models contain no
  telemetry references (`RouteTelemetry_NoTelemetryReferencesInNativeOrModels`).
- No OpenTelemetry packages; no exporter; no appsettings change; no ILogger
  change. `Program.cs` only wires the decorator.

## Explicit non-goals (this phase)

- Per-route spans / per-route metrics / batch-size tags.
- Attaching destination prefixes, gateways, interfaces, route identities, or
  command text.
- OpenTelemetry packages or exporters.
- A `route_kind` discriminator on `ManagedRoute` (would be a model change; the
  native boundary cannot authoritatively determine kind, so `unknown` is used).
- A dedicated timeout unit test (the 30 s / 5 min command timeout path surfaces
  as `TimeoutException` and is covered by the failure-category mapping, but no
  new artificial timeout injection point was added).

## Next phase

With the runtime-cycle root, planning, execution, and the native route boundary
now instrumented, remaining runtime-cycle child operations
(`Runtime.Observe`, `Runtime.BuildDecision`, `Runtime.BuildPreview`,
`Runtime.PersistInventory`) and the prefix/DNS/IPC/support spans can follow the
same narrow-wrapper pattern in separately-scoped phases.
