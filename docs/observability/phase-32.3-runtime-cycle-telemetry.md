# Phase 32.3 — Runtime cycle tracing and metrics

Instruments exactly one production workflow — the top-level runtime
reconciliation cycle — using the Phase 32.2 telemetry contracts. No planner
internals, route operations, DNS, prefix updates, IPC, or support exports are
instrumented. No OpenTelemetry packages, no exporters, no DI/appsettings
changes.

--------------------------------------------------
1. Production boundary instrumented
--------------------------------------------------

The complete cycle lifecycle is owned by three entry methods on
`IranDirectController`:

- `EnableAsync`   — user-initiated enable  (trigger **forced**)
- `DisableAsync`  — user-initiated disable (trigger **forced**)
- `RunCycleAsync` — auto-repair / on-demand (trigger **repair**)

Each method already called `_profiler.BeginCycleIfNone(...)` and wrapped the
body in `try/catch (OperationCanceledException) / catch (Exception)` that
re-throws after recording the profiler outcome. The telemetry root activity and
counters/histogram are introduced symmetrically: a
`RuntimeCycleTelemetryScope` is opened alongside the profiler scope and
completed in the same catch branches, preserving the existing re-throw
semantics exactly.

`RunCycleCoreAsync` (the private method) is intentionally NOT instrumented —
it is not the complete-cycle boundary and must not create a second span.

--------------------------------------------------
2. Activity lifecycle
-------------------------------------------------

Exactly one root activity per cycle:

- Source:   `IranDirectTelemetry.ActivitySource`
- Name:     `IranDirectActivityNames.RuntimeCycle` ("IranDirect.RuntimeCycle")
- Kind:     `ActivityKind.Internal`
- Lifetime: `using RuntimeCycleTelemetryScope` in each entry method.

Tags set on the activity:

- `operation`  = `runtime_cycle`        (set at start)
- `trigger`    = `scheduled|forced|startup|repair|unknown` (set at start)
- `outcome`    = `success|failure|cancelled|timeout|no_change` (set at terminal)
- `failure_category` = bounded value, ONLY on failure/timeout (set at terminal)

No child spans are created in this phase. No exception events are recorded
(Phase 32.1 did not approve them). `ActivitySource.StartActivity` returning
null (no listener) is a fully supported fast path.

--------------------------------------------------
3. Instruments created (Phase 32.2 Meter)
-------------------------------------------------

All created once as static fields in `RuntimeCycleTelemetry`:

| Name                                  | Type             | Unit | Description |
|---------------------------------------|------------------|------|-------------|
| `irandirect.runtime.cycles.started`   | `Counter<long>`  | {cycle} | Cycles that started. |
| `irandirect.runtime.cycles.completed` | `Counter<long>`  | {cycle} | Cycles completed (success or no-change). |
| `irandirect.runtime.cycles.failed`    | `Counter<long>`  | {cycle} | Cycles failed or timed out. |
| `irandirect.runtime.cycles.cancelled`| `Counter<long>`  | {cycle} | Cycles cancelled. |
| `irandirect.runtime.cycle.duration`   | `Histogram<double>` | ms | Cycle duration in milliseconds. |

No `Observable*` instruments. No second Meter. No dynamic instrument creation.

--------------------------------------------------
4. Trigger mapping
-------------------------------------------------

| Caller            | Trigger enum        | Tag value  |
|-------------------|---------------------|------------|
| `EnableAsync`     | `TelemetryTrigger.Forced`  | `forced`  |
| `DisableAsync`    | `TelemetryTrigger.Forced`  | `forced`  |
| `RunCycleAsync`   | `TelemetryTrigger.Repair`  | `repair`  |

`scheduled` / `startup` are reserved for future entry points (hosted-service
tick, startup) and map to `unknown` if a new caller cannot be classified
safely. Triggers are derived ONLY from the known entry point, never from
free-form strings, caller names, or stack inspection.

--------------------------------------------------
5. Outcome / status mapping
-------------------------------------------------

| Runtime result                          | Activity outcome | Status   | Counter         |
|-----------------------------------------|------------------|----------|-----------------|
| Completed / Planned w/ mutation         | `success`        | Ok       | `completed`     |
| NoExecutionRequired / Completed w/o mutation | `no_change` | Ok       | `completed`     |
| `OperationCanceledException`            | `cancelled`      | Unset    | `cancelled`     |
| `IOException`, routing fault, other    | `failure`        | Error    | `failed`        |
| `TimeoutException` (if reachable)       | `timeout`        | Error    | `failed`        |

Outcome is decided by the controller's existing exception/catch structure, not
by inspecting result text. `PartialCompletion` maps to `failure` (per Phase
32.2). Exactly one terminal counter is recorded per cycle; duration is recorded
once regardless of outcome.

--------------------------------------------------
6. Failure-category behavior
-------------------------------------------------

On failure, `TelemetryFailureCategoryMapper.Map(exception)` classifies the
exception type into a bounded `TelemetryFailureCategory`, emitted as both the
activity `failure_category` tag and the metric `failure_category` tag (on
`failed` records only). The exception `.Message` is NEVER used as a tag value or
status description. Cancellation sets no `failure_category`.

Mapped in tests: `IOException` → `io`; `FaultInjectionException` (RouteCreate)
→ `routing`; `InvalidOperationException` → `unknown`.

--------------------------------------------------
7. Cancellation / timeout behavior
-------------------------------------------------

- Cancellation: `OperationCanceledException` propagates unchanged (existing
  behavior). Outcome `cancelled`, status left `Unset`, `cancelled` counter +1,
  `completed`/`failed` not incremented, duration recorded once.
- Timeout: `TimeoutException` is classified to `timeout` outcome + `timeout`
  failure-category, `failed` counter +1, activity status `Error`. (No dedicated
  timeout path exists in the controller today; the mapping is wired through the
  exception-type mapper and would trigger if such an exception reaches the
  boundary. No timeout path was invented.)

--------------------------------------------------
8. Duration measurement design
-------------------------------------------------

The controller has no `TimeProvider`. The scope measures the cycle boundary
with `Stopwatch.GetTimestamp()` at construction and
`Stopwatch.GetElapsedTime(...).TotalMilliseconds` at terminal recording —
consistent with the existing `RuntimeCycleProfiler` (which uses `Stopwatch`
internally). The two measurements are independent (least-coupled option per
§11/§12); neither reads the other's result and neither mutates
`RuntimePerfReport` semantics.

Duration is recorded in `RecordTerminal` (success/no-change/failure/cancelled)
and also in `Dispose` as a safety net if a terminal method is somehow skipped,
so duration is never dropped. The `Interlocked` guard prevents double-counting
terminal counters.

--------------------------------------------------
9. RuntimePerfReport relationship
-------------------------------------------------

Preserved exactly (Phase 32.1 decision: report stays independent, telemetry
mirrors the same boundary):

- No `RuntimePerfReport` model changes.
- No `RuntimePerfReportStore` changes.
- No telemetry dependency inside report models.
- Telemetry never reads the report; it measures its own boundary.
- Tests assert the profiler still writes a report file alongside telemetry.

--------------------------------------------------
10. No-listener behavior
-------------------------------------------------

With no `ActivityListener` and no `MeterListener`:

- `StartActivity` returns null; the `using` scope handles null activity
  gracefully (all `_activity is not null` guards).
- Counters/histogram `Add`/`Record` are no-ops when no listener subscribes to
  the Meter (BCL behavior).
- Runtime result is returned unchanged; no exception is thrown by telemetry.
- A no-op cycle with no listeners completes and allocates no route-sized
  structures (see allocation test).

--------------------------------------------------
11. Privacy / cardinality compliance
-------------------------------------------------

- Allowed tags: `operation`, `trigger`, `outcome`, `failure_category` (metrics
  only on failed/timeout). All bounded enums/constants.
- Prohibited tags (route identities, prefixes, gateways, interface names, DNS
  domains, file paths, pipe data, machine/user names, URLs, arbitrary IDs) are
  never attached. Verified by the `ProhibitedTagConstants_DoNotAppearInProductionSource`
  architecture test (extended in Phase 32.2) and by the prohibited-tag scan.
- Exception text is excluded from all tags/status.
- Cardinality is bounded: 4 trigger × 5 outcome × ~9 failure-category values.

--------------------------------------------------
12. Tests added
-------------------------------------------------

`IranDirect.Core.Tests/Observability/Telemetry/RuntimeCycleTelemetryTests.cs`
(10 facts, non-parallel collection to avoid static-instrument cross-talk):

- Success-with-changes: activity + counters + duration + tags + status Ok.
- No-execution-required: outcome `no_change`, status Ok.
- Failure (IOException): outcome `failure`, status Error, category `io`,
  exception text absent from tags, counters exact.
- Routing fault (FaultInjectionException): category `routing`.
- Unknown exception (InvalidOperationException): category `unknown`.
- Cancellation: outcome `cancelled`, `cancelled` counter, status Unset, no
  failure_category, duration recorded.
- No-listener: result unchanged, no exception.
- Enable trigger mapped to `forced`.
- Profiler report still produced (telemetry did not alter it).
- No-listener allocation overhead reported (bounded, no brittle threshold).

`TelemetryArchitectureTests.cs` extended:

- `ExactlyOneRuntimeCycleStartActivity_NoChildSpans` — one
  `StartActivity(...RuntimeCycle)` in production, no dynamic span names.
- `ExactlyFiveRuntimeCycleInstruments` — 4 counters + 1 histogram.
- `Controller_OnlyApprovedRuntimeCycleInstrumentation` — controller only calls
  `RuntimeCycleTelemetry.Start(TelemetryTrigger...)`, creates no ActivitySource/
  Meter/instruments.

--------------------------------------------------
13. Verification evidence
-------------------------------------------------

- `dotnet build` (full solution): 0 warnings, 0 errors.
- Focused telemetry/runtime suite: 128 passed, 0 failed.
- Full `IranDirect.Core.Tests` suite: green (existing controller/profiler/
  report/cancellation/fault-injection/lifecycle tests preserved).
- Stress (`Category=Stress`): green.
- Benchmark Release build: 0/0.
- Architecture searches: one production `StartActivity` (RuntimeCycle), no child
  spans, no other workflow metrics, no OpenTelemetry packages, no prohibited
  tags.

--------------------------------------------------
14. Non-goals (explicit)
-------------------------------------------------

- No planner-internal, route, prefix, DNS, IPC, or support-export spans.
- No child spans of `IranDirect.RuntimeCycle`.
- No OpenTelemetry packages / exporters / DI / appsettings changes.
- No `RuntimePerfReport` changes.

--------------------------------------------------
15. Phase 32.4 next scope (proposed)
-------------------------------------------------

- Child spans for cycle sub-steps (Observe, BuildDecision, PlanChanges, Execute,
  PersistInventory) — pending Phase 32.1 workflow map approval.
- Per-route-kind / execution-step metric tags (only if cardinality stays
  bounded and approved).
- Additional triggers (scheduled/startup) when the hosted-service entry point is
  instrumented.
- Wiring an OpenTelemetry exporter behind the existing ActivitySource/Meter
  (separate phase, separate package addition).
