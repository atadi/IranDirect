# IranDirect Current Architecture

This document is the committed executive summary for new contributors and AI sessions.

Read it immediately after `AI-START-HERE.md`.

## Current Mission

IranDirect is a Windows network control plane that:

- routes Iranian IPv4 prefixes directly through the ISP gateway;
- leaves all other traffic on the VPN path;
- guarantees that IranDirect routing never breaks VPN endpoint connectivity.

## Current Branch

The active development branch has historically been:

```text
development/service-authority
```

Always verify the actual checked-out branch before continuing.

## Current Control Flow

```text
DesiredConfiguration
        |
        v
RuntimeCoordinator
   |             |
   v             v
RuntimeObserver  RuntimePlanner
   |             |
   v             v
ObservedRuntime  DesiredRuntime
        \       /
         \     /
      RuntimePlanSnapshot
              |
              v
     RuntimeReconciler
              |
              v
   RuntimeChangeSet
              |
              v
  RuntimeExecutionPlanner   (pure, deterministic, safe ordering)
              |
              v
    RuntimeExecutionPlan
              |
              v
 RuntimeDecisionBuilder    (composes lifecycle artifacts)
              |
              v
      RuntimeDecision       (domain contract)
              |
              v
 RuntimeCycleCoordinator   (use-case entry point)
              |
              v
      RuntimeExecutor
              |
              v
  Windows Networking Platform
```

## Implemented Responsibilities

- Windows Service as sole mutation authority
- Named-pipe IPC for CLI and Tray
- explicit route ownership inventory
- VPN endpoint discovery
- VPN endpoint route protection
- endpoint lifecycle and health
- read-only diagnostics
- desired configuration persistence
- runtime observation
- runtime planning
- runtime coordination
- read-only runtime plan exposure
- read-only runtime cycle coordination
- execution domain model (step, plan, result)
- execution planner (pure RuntimeChangeSet → RuntimeExecutionPlan translation)
- pre-execution decision contract (RuntimeDecision with validated consistency)
- pre-execution decision builder (RuntimeDecisionBuilder composing plan, reconciliation, execution plan into validated RuntimeDecision)
- coordinator migration to RuntimeDecision (RuntimeCycleCoordinator delegates to IRuntimeDecisionBuilder, returns RuntimeDecision; RuntimeCycleResult removed)
- execution pipeline (RuntimeExecutor with sequential stop-on-failure step processing)
- Windows route execution handler (mutate → verify → persist for add/remove prefix and endpoint route steps)
- inventory persistence interfaces (IRouteInventoryPersistence, IEndpointInventoryPersistence)
- production enable/disable via RuntimeDecision + RuntimeExecutor pipeline
- controller-orchestrated execution cycle (IranDirectController delegates to coordinator and executor)
- service startup cycle (enabled desired → run one cycle on boot)
- periodic repair loop (AutoRepair + RepairInterval)
- single-flight overlap guard (OperationCoordinator shared across worker + IPC)
- physical gateway refresh every cycle (GatewayDetector runs per observation)
- structured per-cycle logging (trigger, status, steps, duration)
- failure isolation (failed cycles don't terminate the service)
- LastError management (cleared on success, preserved on failure)
- automated tests
- Architecture Knowledge Base
- deterministic AI bootstrap
- generated local-state handoff

## Current Architectural Debt

The legacy `RouteReconciler` class and its DI registration are removed (zero production consumers). The `RouteInventoryStore.ClearAsync` call during disable duplicates pipeline inventory cleanup. The `OpenVPN` endpoint protection path has not been validated with ZoogVPN active.

## Immediate Next Milestone

Complete one-prefix manual verification on real Windows to confirm:
- startup cycle when enabled
- periodic repair restores deleted routes
- stale InterfaceIndex is corrected
- restart recovery is idempotent
- disable removes owned routes
Then tag the release candidate.

## Non-Negotiable Invariants

1. VPN safety has absolute priority.
2. The Windows Service is the only route-mutation authority.
3. IranDirect removes only explicitly owned routes.
4. Observation is read-only.
5. Planning is pure.
6. Diagnostics are read-only.
7. Reconciliation is idempotent.
8. Blocked plans never mutate infrastructure.
9. Configuration, observed runtime, desired runtime, inventory, and diagnostics remain distinct.
10. Architectural changes are introduced in small testable slices.

## Required Verification

Before implementation, establish one of:

- direct checkout and terminal access; or
- a fresh uploaded `AI-LOCAL-STATE.md`.

Never infer the developer's current branch, working tree, build, tests, services, or route table from the public repository alone.