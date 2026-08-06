# Runbook: IranDirectRuntimeCycleFailureRateHigh / IranDirectRuntimeCycleFailureRateCritical

- **Alert:** `IranDirectRuntimeCycleFailureRateHigh / IranDirectRuntimeCycleFailureRateCritical`
- **Severity:** warning / critical
- **Dashboard:** `irandirect-reliability-errors`
- **Prometheus query:** `sum(rate(irandirect_runtime_cycles_failed_total[10m])) / sum(rate(irandirect_runtime_cycles_started_total[10m]))`

## What it means
A high share of runtime reconciliation cycles are failing (>10% warning, >30% critical over 10m).

## User impact
Desired-state convergence is partially (warning) or largely (critical) failing. Routes may drift from the configured desired state.

## Dashboard
Open the `irandirect-reliability-errors` dashboard in the IranDirect folder (Grafana).
Prometheus query: `sum(rate(irandirect_runtime_cycles_failed_total[10m])) / sum(rate(irandirect_runtime_cycles_started_total[10m]))`

## Symptoms
Reliability dashboard 'Runtime cycle failures and cancellations' rising; success ratio dropping.

## Likely causes
Transient infrastructure during reconcile; planner/executor exception; downstream API throttling; a bad desired-state change being repeatedly rejected.

## Safe checks
Open the Runtime Reconciliation dashboard; correlate failures with planning vs execution p95; check Tempo traces for the failing cycles (Explore -> service.name=IranDirect.Service).

## Corrective actions
Identify the failing phase from traces. If a specific desired-state change is rejected, fix or roll back that change. If throttling, back off the reconcile trigger. Treat critical as a site-affecting incident.

## What not to do
Do not disable reconciliation to stop the alert - that makes drift permanent. Do not bulk-rollback desired state without identifying the offending change.

## Escalation criteria
Critical sustained >15m: page the Service owner and the change author of any recent desired-state update.

## Evidence to preserve
Failing cycle traces in Tempo, the change that correlates with failure onset, success-ratio history.

## Resolution verification
Failure ratio returns below 10% and cycles complete successfully.

## Related alerts
IranDirectRuntimeCycleLatencyHigh, IranDirectRouteOperationFailureRatioHigh

## Ownership
Service / SRE
