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

## Next

- Implement RuntimeExecutionPlanner to convert RuntimeChangeSet to ordered RuntimeExecutionPlan.
- Implement RuntimeExecutor for platform operations.
- Reduce the controller to orchestration.
- Move route and inventory execution into focused executors.