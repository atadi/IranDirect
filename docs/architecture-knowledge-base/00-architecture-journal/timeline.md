# Architecture Journal Timeline

## Foundation

- Service becomes the single authority.
- CLI and Tray become IPC clients.
- Persistence moves to shared ProgramData storage.

## Route Ownership

- Route reconciliation becomes idempotent.
- Route inventory records explicit ownership.
- Disable removes owned routes only.

## VPN Safety

- OpenVPN endpoints are discovered from profiles.
- Endpoint routes are protected before prefix routes.
- Endpoint inventory and lifecycle are introduced.

## Stabilization

- Automated tests are added.
- Diagnostics become read-only and structured.
- Desired configuration becomes explicit.

## Control-Plane Evolution

- `ObservedRuntime` describes facts.
- `RuntimePlanner` produces `DesiredRuntime`.
- `RuntimeCoordinator` composes configuration, observation, and planning.
- Runtime plans become visible through CLI before execution.

## Execution Domain

- Runtime execution domain model defined (step kinds, plan, result statuses).
- RuntimeChangeSet becomes the input for execution planning rather than direct execution.
- Execution ordering policy established: AddEndpointRoute → RemovePrefixRoute → AddPrefixRoute → RemoveEndpointRoute.
- Safety invariant: establish new endpoint protection first; remove obsolete prefix routes before adding replacements; keep endpoint protection until all prefix work is complete; remove obsolete endpoint protection last.

## Execution Planner

- RuntimeExecutionPlanner implemented: pure, stateless, explicit ordering policy.
- Planning uses AddEndpointRoute → RemovePrefixRoute → AddPrefixRoute → RemoveEndpointRoute priority.
- Within-group sorting by identity with OrdinalIgnoreCase.
- Enum ordinal values never define execution order.
- Planning produces no side effects, no execution results, no status flags.
- 18 tests verify ordering, mapping, field fidelity, duplicate preservation, null safety, and unsupported-kind rejection.

## Decision Contract

- RuntimeDecision introduced: immutable, validated domain contract for the complete pre-execution decision.
- Enforces: plan non-null, reconciliation non-null, execution-plan non-null, non-default DecidedAt.
- Consistency rules: NoChangesRequired → empty plan; ChangesPlanned → non-empty with traceability; Blocked/Failed → empty plan; ChangesApplied → rejected (pre-execution contract).
- Traceability: change-to-step count equality + sorted (identity, kind) pair correspondence — validates without duplicating planner logic.
- 24 tests verify all invariants, null safety, default rejection, reference preservation, identity/kind mismatch detection, duplicate handling, and non-recalculation of ordering.
- Domain Decision concept distinguished from DTO: lifecycle boundaries need validated domain decisions, not transport-shaped data.

## Decision Builder

- RuntimeDecisionBuilder introduced: stateless, read-only orchestrator that composes the existing lifecycle into a validated RuntimeDecision.
- Orchestration flow: IRuntimePlanCoordinator.BuildPlanAsync → IRuntimeReconciler.ReconcileAsync → RuntimeExecutionPlanner.Plan → TimeProvider.GetUtcNow → RuntimeDecision.Create.
- No status-specific branching — RuntimeDecision.Create owns all consistency validation.
- Injected TimeProvider for deterministic, testable timestamps; no DateTimeOffset.UtcNow in the builder.
- Interface IRuntimeDecisionBuilder introduced as the authoritative pre-execution boundary, justified by future RuntimeCycleCoordinator migration and potential IPC/worker exposure.
- 24 tests verify exact reference preservation, token forwarding, cancellation propagation, planner exception propagation, all status consistency paths, time source injection, and constructor validation.
- DI registrations: RuntimeExecutionPlanner singleton, TimeProvider.System singleton, IRuntimeDecisionBuilder→RuntimeDecisionBuilder singleton.

## Next

- Migrate RuntimeCycleCoordinator to use RuntimeDecisionBuilder and return RuntimeDecision, replacing RuntimeCycleResult as the authoritative pre-execution artifact.
- Implement RuntimeExecutor for platform operations.
- Reduce the controller to orchestration.
- Move route and inventory execution into focused executors.