# IranDirect — Legacy Architecture

IranDirect is the historical predecessor of PathVeer.

This directory preserves the early architectural evolution that led to the current PathVeer control plane. It is not the authority for current product identity, current architecture status, or the immediate engineering milestone.

For current durable architecture see:

`../PathVeer/README.md`

For current engineering state see:

`../../AI/CURRENT.md`

## Historical Mission

IranDirect originally maintained direct routing for Iranian IPv4 prefixes while guaranteeing that VPN endpoint connectivity was not compromised.

Its key architectural contributions included:

- moving mutation authority into one Windows Service;
- named-pipe intent from CLI/Tray;
- explicit route ownership;
- VPN endpoint protection;
- observe -> plan -> reconcile control-loop architecture;
- deterministic execution planning/execution;
- persistence/recovery and runtime coordination.

These concepts evolved into PathVeer's country-configurable control plane.

## Historical Invariants

The following principles survived the rename/generalization:

1. one machine route-mutation authority;
2. VPN endpoint reachability before prefix routing;
3. remove only explicitly owned resources;
4. diagnostics/observation remain read-only;
5. planning remains pure/deterministic;
6. reconciliation remains idempotent;
7. blocked plans do not mutate infrastructure.

See [evolution.md](evolution.md) for the chronological architecture record.
