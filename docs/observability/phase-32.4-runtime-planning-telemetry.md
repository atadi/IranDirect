# Phase 32.4 — Runtime planning telemetry

Instruments exactly one additional runtime operation — change-set planning
(`RuntimeChangeSetPlanner.Plan`) — as a child of the runtime-cycle Activity
from Phase 32.3. No new root span, no planner lifecycle counters, no
OpenTelemetry packages/exporters, no changes to planner semantics or models
beyond a testability seam.

--------------------------------------------------
1. Planner boundary selected
-------------------------------------------------

The single production invocation of `RuntimeChangeSetPlanner.Plan(...)` lives
in `RuntimeReconciler.ReconcileAsync` (the runtime reconciliation orchestration
owner). Tracing the call chain:

- `IranDirectController.EnableAsync/DisableAsync/RunCycleAsync`
  → `RunCycleCoreAsync` (starts the `IranDirect.RuntimeCycle` root activity)
  → `RuntimeCycleCoordinator.RunCycleAsync` → `RuntimeDecisionBuilder.BuildAsync`
  → `IRuntimeReconciler.ReconcileAsync` → `RuntimeReconciler.ReconcileAsync`
  → `_changeSetPlanner.Plan(snapshot, ownership)`.

`Plan` is called exactly once per applicable cycle and runs **synchronously**
inside the active runtime-cycle Activity, so `Activity.Current` is the
`IranDirect.RuntimeCycle` activity during planning — the child
`Runtime.PlanChanges` nests correctly.

The other `.Plan(` call sites (`RuntimeCoordinator._planner.Plan`,
`RuntimeDecisionBuilder._executionPlanner.Plan`) use different planner types
(`RuntimePlanner`, `RuntimeExecutionPlanner`) and are out of scope.

--------------------------------------------------
2. Parent/child hierarchy
-------------------------------------------------

    IranDirect.RuntimeCycle        (root, Phase 32.3)
    └── Runtime.PlanChanges        (child, Phase 32.4)

`Runtime.PlanChanges` is created via
`IranDirectTelemetry.ActivitySource.StartActivity(IranDirectActivityNames.RuntimePlanChanges)`
and automatically nests under `Activity.Current`. If planning were ever invoked
without an active parent (not the case in current production), it would
naturally become a root activity — no fake parent is manufactured.

--------------------------------------------------
3. Instruments created
-------------------------------------------------

Both created once, statically, against the Phase 32.2 Meter:

| Name                              | Type              | Unit     | Description |
|-----------------------------------|-------------------|----------|-------------|
| `irandirect.runtime.planning.duration` | `Histogram<double>` | ms  | Elapsed time producing the change set. |
| `irandirect.runtime.changed_routes`   | `Histogram<double>` | {route} | Authoritative number of planned runtime changes. |

No planner lifecycle counters (`planner.started/completed/failed`) — those
names are absent from the Phase 32.2 catalog and are intentionally not
invented. `change_bucket` is also absent from the approved catalog, so it is
NOT added (per Phase 32.1/32.2 design); raw change counts flow only into the
`changed_routes` histogram, which does not create label cardinality.

--------------------------------------------------
4. Operation / outcome mapping
-------------------------------------------------

- `operation = plan_changes` — added to the bounded operation-value catalog
  (`IranDirectTagValues.OperationPlanChanges`); passes the existing
  `TelemetryTagValidator` contract (constant, no free-form values).
- success: planning completes with `changeSet.Count > 0`.
- no_change: planning completes with `changeSet.Count == 0` (empty change set).
- failure: any exception thrown by `Plan` (mapped via
  `TelemetryFailureCategoryMapper`).
- cancelled / timeout: NOT real planner paths (see §9).

--------------------------------------------------
5. Change-count measurement and bucketing decision
-------------------------------------------------

- `changed_routes` histogram value = `changeSet.Count` (O(1) property, no
  re-enumeration). Recorded exactly once on success/no-change.
- On zero changes: records `0`, outcome `no_change`.
- On failure (planner threw): no `changed_routes` sample (no authoritative
  result), only duration + failure outcome.
- `change_bucket` was evaluated against the Phase 32.1/32.2 catalog and is NOT
  approved, so it is deliberately omitted. Only the numeric histogram is used.

--------------------------------------------------
6. Failure-category behavior
-------------------------------------------------

`TelemetryFailureCategoryMapper.Map(exception)` classifies the thrown
exception type into a bounded `TelemetryFailureCategory`:

- `IOException` → `io`
- `FaultInjectionException` (route point) → `routing`
- `InvalidOperationException` (or any other) → `unknown`

The category is set as the activity `failure_category` tag and (implicitly via
the shared mapper) the metric tag space; the exception `.Message` is NEVER used
as a tag value or status description.

--------------------------------------------------
7. Cancellation / timeout findings
-------------------------------------------------

`RuntimeChangeSetPlanner.Plan` is **synchronous and accepts no
CancellationToken**. In `RuntimeReconciler.ReconcileAsync`, the planner
invocation is wrapped in a `try/catch (Exception)` that **swallows** any thrown
exception and converts it to `RuntimeReconciliationResult.Failed(...)` — the
original exception does not propagate past the reconciler. Therefore:

- A planner-path `OperationCanceledException` is not reachable (the planner
  cannot be cancelled mid-execution, and even if it were, the catch converts it
  to a `Failed` result rather than rethrowing as cancellation).
- A planner-path `TimeoutException` is not reachable.

Per Phase 32.4 §17, cancellation/timeout planner tests are NOT fabricated. The
telemetry `CompleteFailure` mapper still handles those exception types
generically (harmless), and the architecture remains prepared through the
shared mapper. The reconciler's existing failure/recovery boundary is fully
preserved: ownership-load failures (which occur *before* `Plan`) continue to
produce a `Failed` reconciliation result with no planning activity emitted.

--------------------------------------------------
8. Duration design
-------------------------------------------------

The `RuntimePlanningTelemetryScope` measures the boundary with
`Stopwatch.GetTimestamp()` at construction and
`Stopwatch.GetElapsedTime(...).TotalMilliseconds` at terminal recording — same
approach as the runtime-cycle scope (the controller has no `TimeProvider`, and
`RuntimeReconciler` likewise has none). Duration is recorded once per started
planning operation on every terminal path (success, no_change, failure) and
also via a `Dispose` safety net. An `Interlocked` guard prevents double
recording.

--------------------------------------------------
9. No-listener behavior
-------------------------------------------------

With no `ActivityListener`: `StartActivity` returns null; the scope handles the
null activity via `_activity is not null` guards. With no `MeterListener`:
`Histogram.Record` is a no-op (BCL behavior). Planning results are returned
unchanged, exceptions/cancellation behavior identical, and no
route-sized allocation is introduced by telemetry. A no-listener test asserts
the reconciliation result is identical with and without a listener.

--------------------------------------------------
10. Planner benchmark before/after
-------------------------------------------------

The committed Phase 30.3 baseline (from `p303-validation-run1.md`) for
`RuntimeChangeSetPlannerBenchmarks.Plan` at **50K Mixed** was:

    Allocated = 18384.26 KB

This phase instruments planning only through `RuntimeReconciler.ReconcileAsync`.
The BenchmarkDotNet job calls `_planner.Plan(...)` **directly**, bypassing the
reconciler and therefore the telemetry wrapper entirely. The planner hot path
is byte-for-byte unchanged. A Release re-run of
`*RuntimeChangeSetPlannerBenchmarks*` reproduces the 50K Mixed Allocated value
to the same integer (well within the ≤5% allocation budget, in fact 0%
difference). Runtime Mean variance on this host exceeds 30% between runs, so no
runtime-win claim is made; allocation is the decision metric and it is
identical.

--------------------------------------------------
11. Privacy / cardinality evidence
-------------------------------------------------

- Approved tags only: `operation`, `outcome`, `failure_category` (on failure).
- `change_bucket` is not used; raw counts go only to a numeric histogram.
- Prohibited tags (destination_prefix, gateway, next_hop, interface_index,
  interface_name, route_identity, execution_step_identity, diagnostic_id,
  domain, url, output_path, exception_message, …) are never attached.
- Exception `.Message` is excluded from all tags/status.
- Cardinality is bounded: 1 operation × 3 outcomes (success/no_change/failure)
  × ~9 failure categories.

Verified by the parent/child architecture tests and the prohibited-tag scan.

--------------------------------------------------
12. Tests added / modified
-------------------------------------------------

`IranDirect.Core.Tests/Runtime/Reconciliation/RuntimeReconcilerTests.cs`
(extended, 13 new facts, non-parallel collection):

- one `Runtime.PlanChanges` child activity emitted with correct name/kind/
  operation tag;
- no-change → outcome `no_change`, status Ok;
- planning failure path (IOException → `io`, routing fault → `routing`,
  unknown → `unknown`), status Error, category set, exception text absent;
- ownership-load failure before `Plan` does not leak a planning success;
- planning-duration histogram recorded exactly once;
- changed-routes histogram recorded exactly once (count>0 on changes, 0 on
  no-change);
- no-listener result unchanged.

`IranDirect.Core.Tests/Observability/Telemetry/TelemetryArchitectureTests.cs`
(extended): exactly one `Runtime.PlanChanges` `StartActivity`; no dynamic span
names; exactly two planning histograms; no planner lifecycle counters; planner
implementation/model stays telemetry-free (reconciler owner excluded from the
planner-namespace scan).

A small testability seam was added to `RuntimeChangeSetPlanner`: the class is
no longer `sealed` and `Plan` is `virtual`, so a `ThrowingPlanner` test fake can
override it. This does not alter planner behavior or instrumentation.

--------------------------------------------------
13. Verification evidence
-------------------------------------------------

- `dotnet build` (full solution): 0 warnings, 0 errors (in changed files;
  pre-existing warnings live in unrelated projects CLI/Tray/Service).
- Focused suite (RuntimePlanningTelemetry + RuntimeChangeSetPlanner +
  RuntimeCycleTelemetry + Observability.Telemetry): 112 passed, 0 failed.
- Full `IranDirect.Core.Tests` suite: green.
- Planner equivalence / adversarial / decision / execution / profiler / report
  / cancellation / fault-injection / stress tests: preserved, green.
- Stress (`Category=Stress`): green.
- Benchmark Release build: 0/0.
- Planner benchmark (`*RuntimeChangeSetPlannerBenchmarks*`): 50K Mixed
  Allocated reproduces baseline (0% allocation regression; ≤5% accepted).

--------------------------------------------------
14. Non-goals (explicit)
-------------------------------------------------

- No child spans beneath `Runtime.PlanChanges`.
- No planner lifecycle counters.
- No `change_bucket` tag (not approved).
- No OpenTelemetry packages / exporters / DI / appsettings changes.
- No `RuntimeChangeSetPlanner` model or algorithm changes.
- No instrumentation of RuntimeDecisionBuilder, RuntimeExecutor, route
  operations, prefix/DNS/IPC/support export, CLI/Tray/Service.

--------------------------------------------------
15. Next phase (proposed)
-------------------------------------------------

- Child spans for remaining runtime-cycle sub-steps (Runtime.Observe,
  Runtime.BuildDecision, Runtime.Execute, Runtime.PersistInventory) — pending
  Phase 32.1 workflow-map approval.
- Bounded `change_bucket` only if Phase 32.1 design is explicitly extended.
- Scheduled/startup triggers when the hosted-service entry point is
  instrumented.
- OpenTelemetry exporter behind the existing ActivitySource/Meter (separate
  phase, separate package).
