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
- automated tests
- Architecture Knowledge Base
- deterministic AI bootstrap
- generated local-state handoff

## Current Architectural Debt

The controller still owns reconciliation and execution decisions that should move into a dedicated runtime reconciler.

## Immediate Next Milestone

Introduce `RuntimeReconciler` as a stable execution contract.

Initial goals:

1. consume `RuntimePlanSnapshot`;
2. reject blocked plans without mutation;
3. compute minimal changes;
4. delegate platform operations through existing route and inventory components;
5. preserve all current behavior;
6. keep the controller thin.

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