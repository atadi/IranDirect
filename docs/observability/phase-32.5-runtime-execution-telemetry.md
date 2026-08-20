# Phase 32.5 — Runtime Execution Telemetry

Instruments exactly one additional runtime operation — **runtime execution** — as
a child of the `IranDirect.RuntimeCycle` activity introduced in Phase 32.3.

This is the third telemetry slice over the runtime reconciliation cycle:

```
IranDirect.RuntimeCycle            (root, Phase 32.3)
├── Runtime.PlanChanges            (child, Phase 32.4)
└── Runtime.Execute                (child, Phase 32.5)
```

`Runtime.PlanChanges` and `Runtime.Execute` are **siblings** under the
`IranDirect.RuntimeCycle` root. Planning fully stops (its telemetry scope is
disposed inside `RuntimeReconciler.ReconcileAsync`) before execution begins, so
the two never nest. The sibling relationship is proven end-to-end by
`RuntimeExecutionTelemetryTests.SiblingOfPlanning_UnderRuntimeCycle`, which drives
the real controller flow: `IranDirectController.RunCycleAsync` starts the cycle
root, invokes the real `RuntimeReconciler` (emitting `Runtime.PlanChanges`), and
only then wraps `ExecuteAsync` with the `Runtime.Execute` producer.

## Execution boundary selected

The narrowest method that owns one complete execution attempt is
`IranDirectController.RunCycleCoreAsync` (the single production invocation of
`IRuntimeExecutor.ExecuteAsync` at the orchestration layer). Telemetry is wrapped
around that call only:

- `RuntimeExecutionTelemetry.Start(planCount)` opens the `Runtime.Execute`
  child activity and records the authoritative O(1) plan-step count.
- On a returned `RuntimeExecutionResult`, `scope.Complete(result)` maps the
  status to a bounded outcome and records exactly one duration + one
  operations-per-cycle sample.
- On a thrown exception, `scope.CompleteFailure(ex)` maps the exception type to a
  bounded failure category.
- On `OperationCanceledException`, `scope.CompleteCancelled()` records the
  cancelled outcome without a failure_category.

`RuntimeExecutor` itself remains **telemetry-free** (no `StartActivity`,
no `Histogram`, no `Observability.Telemetry` reference).

## Parent-child hierarchy

`IranDirect.RuntimeCycle` → `Runtime.PlanChanges` (sibling) → `Runtime.Execute`
(sibling). Both `Runtime.PlanChanges` and `Runtime.Execute` are direct children of
the `IranDirect.RuntimeCycle` root; they are **siblings**, not nested.

- `Runtime.Execute` nests under `Activity.Current` automatically. Because the
  planning telemetry scope is fully disposed inside `RuntimeReconciler.ReconcileAsync`
  before `ExecuteAsync` is invoked, `Activity.Current` at execution time is the
  `IranDirect.RuntimeCycle` activity — so `Runtime.Execute` is a child of the cycle
  and a sibling of `Runtime.PlanChanges`, never a child of planning.
- If invoked without an active runtime-cycle activity it would become a root
  activity; no fake parent is manufactured.
- No child spans under `Runtime.Execute`; no per-step or per-route spans.

## Metric names / types / units

| Metric | Type | Unit | Description |
| --- | --- | --- | --- |
| `irandirect.runtime.execution.duration` | `Histogram<double>` | `ms` | Elapsed time spent executing the runtime plan. |
| `irandirect.runtime.operations.per_cycle` | `Histogram<double>` | `{operation}` | Number of runtime execution operations attempted in one cycle. |

Both created exactly once against `IranDirectTelemetry.Meter`. No execution
lifecycle counters. No per-step instruments.

## Operation / outcome rules

- `operation = execute` (added to the bounded operation catalog as
  `IranDirectTagValues.OperationExecute`). Passes `TelemetryTagValidator`.
- Outcome mapped centrally via `TelemetryOutcomeMapper.Map(RuntimeExecutionResultStatus)`:
  - `Completed` / `Planned` → `success`
  - `NoExecutionRequired` → `no_change`
  - `Failed` → `failure`
  - `Cancelled` → `cancelled`
  - `PartiallyCompleted` → `failure` (no new bounded value introduced)
- `ToString()` is never used.

## Operation-count recording

`irandirect.runtime.operations.per_cycle` is recorded **exactly once** per
started execution attempt, with the O(1) `decision.ExecutionPlan.Count` value
(no enumeration). Value `0` is recorded when an empty plan legitimately reaches
execution (the executor returns `NoExecutionRequired`). The raw count is a
histogram *value*, never a tag.

## Failure-category policy

- Failure category is set **only** when a concrete exception mapping exists
  (via `TelemetryFailureCategoryMapper`): `IOException`→`io`,
  `FaultInjectionException(RouteCreate/RouteDelete/RouteEnumeration)`→`routing`,
  `InvalidOperationException`/other→`unknown`, `OperationCanceledException`→
  `cancellation` (used only on the real cancellation path, not for result
  status).
- When execution failure is represented **only by result status** (returned
  `Failed`/`PartiallyCompleted` results), **no** failure_category is emitted —
  Free-form `ErrorMessage` text is never parsed into a category.

## Cancellation / timeout behavior

- Pre-cancelled token: the fake executor throws `OperationCanceledException`
  before `ExecuteAsync` returns; the controller's existing
  `catch (OperationCanceledException)` rethrows after
  `scope.CompleteCancelled()` → outcome `cancelled`, status `Unset`,
  no failure_category.
- No dedicated timeout path exists in the execution boundary (the orchestration
  relies on `CancellationToken`); therefore no `timeout` test was fabricated.

## No-listener behavior

`StartActivity` returning `null` (no listener) and `Histogram.Record` being a
no-op are both supported fast paths. Behavior is unchanged: executor result,
exception/cancellation propagation, and `RuntimePerfReport`/`RuntimeCycleProfiler`
measurements are untouched. The scope's `Dispose` safety-net records duration +
operations with the `unknown` outcome only if a terminal call was forgotten.

## Scale / stress evidence

The existing executor stress harness (10K/25K/50K) and the
`RuntimeCycleProfiler`/`RuntimePerfReport` path remain the primary correctness
and scale guards. The telemetry wrapper adds only two `Histogram.Record` calls
and one `Stopwatch` read per cycle — no per-step or route-sized allocation.
Acceptance: no executor semantic change, no step-sized telemetry allocation, no
additional concurrency.

## Privacy / cardinality compliance

Only approved tags are emitted:

- `operation` (= `execute`)
- `outcome` (success/failure/cancelled/no_change)
- `failure_category` (only when a concrete exception mapping exists)

Prohibited names (`destination_prefix`, `gateway`, `next_hop`,
`interface_index`, `interface_name`, `route_identity`, `execution_step_identity`,
`command`, `diagnostic_id`, `domain`, `url`, `file_path`, `exception_message`)
never appear. The operation count is a histogram value, never a tag.

## Architecture isolation

- Exactly **one** production `StartActivity` references `RuntimeExecute`
  (`RuntimeExecutionTelemetry.cs`, using the approved
  `IranDirectActivityNames.RuntimeExecute` constant).
- Exactly **two** execution histograms (`execution.duration`,
  `operations.per_cycle`); no execution lifecycle counters; no per-step
  `Histogram.Record` calls.
- `RuntimeExecutor` and execution result/step models contain no telemetry
  references.
- No OpenTelemetry packages; no exporter/DI/appsettings/ILogger changes.

## Explicit non-goals (this phase)

- Per-step spans / per-route spans.
- Instrumenting route-system calls, prefix/DNS/IPC/support export, CLI, Tray, or
  Service hosting.
- OpenTelemetry packages or exporters.
- A generic reusable telemetry-scope framework.
- A dedicated timeout test (no real timeout path at this boundary).

## Next phase

Continue instrumenting the remaining runtime-cycle child operations
(`Runtime.Observe`, `Runtime.BuildDecision`, `Runtime.BuildPreview`,
`Runtime.PersistInventory`) using the same narrow-wrapper pattern, and only then
consider route-system / prefix / DNS spans in a later, separately-scoped phase.
