# Runbook: IranDirectRuntimeCycleLatencyHigh

- **Alert:** `IranDirectRuntimeCycleLatencyHigh`
- **Severity:** warning
- **Dashboard:** `irandirect-runtime-reconciliation`
- **Prometheus query:** `irandirect:runtime_cycle_duration_p95_5m > 5000`

## What it means
Runtime cycle p95 duration exceeds 5s for 15m. (5s is an assumed triage target, not a measured SLO.)

## User impact
Each reconciliation takes longer, so desired-state convergence lags. Under churn, changes propagate more slowly.

## Dashboard
Open the `irandirect-runtime-reconciliation` dashboard in the IranDirect folder (Grafana).
Prometheus query: `irandirect:runtime_cycle_duration_p95_5m > 5000`

## Symptoms
Runtime Reconciliation 'Cycle duration p50/p95/p99' panel elevated; planning/execution p95 high.

## Likely causes
Large route inventory; slow native route calls; GC/CPU pressure on the host; collector backpressure.

## Safe checks
Compare planning vs execution p95 on the dashboard. Check 'p99 latency across operations' on the Reliability dashboard. Check host CPU/RAM.

## Corrective actions
If execution p95 dominates, the native route calls are slow - investigate host load and any recent route growth. If planning dominates, a large change set is being computed each cycle (expected after big changes, transient).

## What not to do
Do not lower the threshold to silence the alert without re-baselining. Do not change the reconcile interval to mask latency.

## Escalation criteria
If p95 stays >30s, escalate to the Service owner - reconciliation may be effectively stalled.

## Evidence to preserve
Dashboard latency history, host resource usage at the time, Tempo traces of slow cycles.

## Resolution verification
Cycle p95 returns below 5s and stays there.

## Related alerts
IranDirectRuntimeCycleFailureRateHigh

## Ownership
Service / SRE
