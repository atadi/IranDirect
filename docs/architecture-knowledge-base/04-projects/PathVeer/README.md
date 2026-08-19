# PathVeer Project Architecture

PathVeer is a Windows network control plane and the successor to IranDirect.

This page describes durable current architecture. It intentionally does not track the current engineering blocker or release artifact; see `../../AI/CURRENT.md` for that.

## Mission

Route configured country IPv4 prefixes through the physical ISP path while leaving other traffic on the VPN path, without breaking VPN endpoint connectivity.

> VPN endpoint connectivity has priority over direct-prefix routing.

## Authority Model

The Windows Service is the only machine route-mutation authority.

```text
CLI ----\
         \
          -> IPC -> PathVeer Service
         /
Tray ---/
```

CLI and Tray express intent. The Service observes, plans, reconciles, executes, verifies, and persists owned state through the established runtime pipeline.

## Control Flow

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
 RuntimeExecutionPlanner
              |
              v
    RuntimeExecutionPlan
              |
              v
 RuntimeDecisionBuilder
              |
              v
      RuntimeDecision
              |
              v
 RuntimeCycleCoordinator
              |
              v
      RuntimeExecutor
              |
              v
 Windows Networking Platform
```

## Core Invariants

1. Service is the sole machine mutation authority.
2. VPN endpoint safety precedes direct-prefix routing.
3. PathVeer removes only resources it can identify as owned.
4. Observation is read-only.
5. Planning is deterministic and side-effect free.
6. Reconciliation is idempotent.
7. Blocked/unsafe plans do not mutate infrastructure.
8. Configuration, requested/effective state, observed/desired runtime, and inventory remain distinct.
9. Tray is per-user UI/controller, not authority.
10. Legacy IranDirect compatibility is preserved only where explicitly required.

## IranDirect Relationship

IranDirect was the original Iran-specific implementation. Its architectural evolution is preserved under `../IranDirect/`.

PathVeer retains selected legacy identifiers and migration contracts so existing state/clients can transition safely. Do not remove those identities solely for branding consistency.

## Durable History

Relevant historical detail is organized under:

- `docs/reliability/`
- `docs/globalization/`
- `docs/branding/`
- `docs/release/`
- `../IranDirect/evolution.md`

For current work, always use `../../AI/CURRENT.md`.
