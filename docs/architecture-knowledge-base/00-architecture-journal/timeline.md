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

## Next

- Introduce runtime reconciliation.
- Reduce the controller to orchestration.
- Move route and inventory execution into focused reconcilers.