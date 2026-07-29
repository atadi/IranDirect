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