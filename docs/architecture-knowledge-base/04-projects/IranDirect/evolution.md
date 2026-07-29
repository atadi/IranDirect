# IranDirect Architecture Evolution

## Phase 1 — Command Utility

```text
CLI
 |
 v
Controller
 |
 v
Windows Routes
```

Problem: multiple processes could own state and route behavior.

## Phase 2 — Service Authority

```text
CLI / Tray
     |
     v
Named Pipe
     |
     v
Windows Service
```

Result: one authority for all mutations.

## Phase 3 — Ownership and Reconciliation

```text
Desired Prefixes
      |
      v
Route Reconciler
      |
      v
Route Inventory
```

Result: idempotent behavior and safe removal.

## Phase 4 — VPN Safety and Diagnostics

```text
VPN Profile
    |
    v
Endpoint Discovery
    |
    v
Endpoint Protection
```

Result: prefix routing cannot override VPN endpoint reachability.

## Phase 5 — Control Plane

```text
Configuration
     |
     v
Observe -> Plan -> Reconcile
```

Result: commands become intent, and infrastructure is continuously reconciled.

## Phase 6 — Execution Domain

```text
Reconciliation
     |
     v
Execution Plan (ordered steps)
     |
     v
Executor -> Verifier -> Inventory
```

Result: deciding what must change, deciding how to change it, performing the change, verifying it, and recording ownership become separate lifecycle stages.

## Phase 7 — Decision Contract

```text
Observation → Planning → Reconciliation → Execution Planning
                                              |
                                              v
                                       RuntimeDecision
                                              |
                              (execute / preview / audit / simulate)
```

Result: the complete pre-execution decision becomes an immutable, validated domain artifact. Consumers no longer reconstruct the decision from intermediate fragments. The contract enforces consistency between reconciliation intent and execution steps.

## Phase 8 — Decision Builder

```text
RuntimePlanSnapshot
       |
       v
RuntimeReconciliationResult
       |
       v
RuntimeExecutionPlan
       |
       v
TimeProvider.GetUtcNow()
       |
       v
RuntimeDecision.Create()
```

Result: a dedicated read-only orchestrator composes the lifecycle into a validated pre-execution decision. The builder owns ordering and reference preservation; the domain contract owns validation. The builder is clock-aware (injected `TimeProvider`) but the domain contract remains clock-independent.

## Phase 9 — Coordinator Migration

```text
RuntimeDecisionBuilder
       |
       v
RuntimeDecision
       |
       v
RuntimeCycleCoordinator   (thin use-case façade)
```

Result: `RuntimeCycleCoordinator` no longer calls `IRuntimePlanCoordinator` or `IRuntimeReconciler` directly. It delegates entirely to `IRuntimeDecisionBuilder`, returning `RuntimeDecision`. `RuntimeCycleResult` is removed — no production consumers remained, and `RuntimeDecision` is the single authoritative pre-execution artifact. The coordinator proves its value as stable use-case vocabulary for "run one runtime cycle" — not as lifecycle logic.

## Phase 10 — Executor

```text
RuntimeDecision
        |
        v
RuntimeExecutor           (sequential, stop-on-failure)
        |
        v
RuntimeExecutionStepHandler  (mutate → verify → persist)
        |
        v
Windows Networking / Inventory
```

Result: the first concrete execution pipeline. `RuntimeExecutor` processes a `RuntimeExecutionPlan` one step at a time, stopping on the first failure. `WindowsRuntimeExecutionStepHandler` performs each platform operation (add/remove prefix routes, add/remove endpoint routes), verifies the result via `IRouteManager`, and persists/removes inventory only after successful verification. Non-owned routes are never removed. `OperationCanceledException` propagates before any success; partial completion is captured as `RuntimeExecutionStatus.PartiallyCompleted`. Inventory persistence follows the existing `RouteInventoryStore` / `VpnEndpointInventoryStore` conventions through minimal extracted interfaces.

## Phase 10.1 — Controller Integration

```text
IPC Enable/Disable
        |
        v
IranDirectController.EnableAsync / DisableAsync
        |
        v
DesiredConfigurationService.SetDesiredEnabledAsync(enabled)
        |
        v
RuntimeCycleCoordinator.RunCycleAsync()
        |
        v
RuntimeDecision  ←  RuntimeDecisionBuilder produces
        |
        v
RuntimeExecutor.ExecuteAsync(plan)
        |
        v
State update on Completed/NoExecutionRequired
```

Result: `IranDirectController.EnableAsync` and `DisableAsync` are replaced with a single consistent pipeline: set desired enabled state → run full observation→plan→reconcile→decision cycle → execute plan → update service state on success. `ServiceResponse` carries the `RuntimeDecision` and `RuntimeExecutionResult` for IPC transparency. The legacy `RouteReconciler` path is no longer called. `RuntimeCycleExecutionResult` bundles the decision and execution result for controller-orchestration use. 15 integration tests cover all execution result statuses, state update policy, the desired-enabled guard, plan dispatch, inventory clearing, and cancellation propagation.

## Phase 10.2 — Service Cycle + Periodic Repair

```text
IranDirectWorker.ExecuteAsync
        |
        +-- startup: load desired config
        |       |
        |       +-- enabled=true  → RunServiceCycleAsync("startup")
        |       +-- enabled=false → skip
        |
        +-- loop (while AutoRepair)
                |
                +-- wait RepairInterval
                +-- RunServiceCycleAsync("periodic")

RunServiceCycleAsync(trigger)
        |
        v
OperationCoordinator.ExecuteAsync  (overlap guard)
        |
        v
IranDirectController.RunCycleAsync  (no desire toggle)
        |
        v
RuntimeCycleCoordinator.RunCycleAsync
        |
        v
IRuntimeExecutor.ExecuteAsync
        |
        v
UpdateStateAsync
    |-- success + desired enabled  → state.Enabled=true, LastError=null
    |-- success + desired disabled → state.Enabled=false, LastError=null
    +-- failure                   → LastError=error, Enabled unchanged
```

Result: two execution paths (IPC commands and service loop) converge on the single `RunCycleAsync` method. The service loop runs one cycle at startup if desired is enabled, then enters a periodic repair loop when `AutoRepair=true`. The `OperationCoordinator` SemaphoreSlim prevents concurrent execution between worker cycles, enable, and disable commands. The `GatewayDetector` runs fresh each cycle (via observation), so stale `InterfaceIndex` or `Gateway` values are corrected automatically. 8 additional controller tests verify `RunCycleAsync` state transitions, LastError management, and gateway refresh. The `RouteReconciler` DI registration is removed (zero production consumers). `tools/Install-IranDirectService.ps1` provides install/uninstall.