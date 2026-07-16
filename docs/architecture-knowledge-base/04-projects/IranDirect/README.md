# IranDirect Architecture

IranDirect is evolving into a state-driven Windows network control plane.

## Mission

Maintain direct routing for Iranian IPv4 prefixes while guaranteeing that VPN endpoint connectivity is never compromised.

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
     RuntimeReconciler (next)
              |
              v
 Windows Networking Platform
```

## Current Invariants

1. The Windows Service is the only route-mutation authority.
2. VPN endpoint reachability has priority over prefix routing.
3. IranDirect removes only explicitly owned routes.
4. Diagnostics never mutate state.
5. Planner and observer are side-effect free.
6. A blocked plan must not be reconciled.