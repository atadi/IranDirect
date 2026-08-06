# Runbook: IranDirectRouteOperationFailureRatioHigh

- **Alert:** `IranDirectRouteOperationFailureRatioHigh`
- **Severity:** warning
- **Dashboard:** `irandirect-runtime-reconciliation`
- **Prometheus query:** `sum(rate(irandirect_routes_operations_failed_total[10m])) / sum(rate(irandirect_routes_operations_requested_total[10m]))`

## What it means
More than 5% of native route system calls failed over 10m.

## User impact
Route add/remove/update operations are partially failing; desired state is not fully applied.

## Dashboard
Open the `irandirect-runtime-reconciliation` dashboard in the IranDirect folder (Grafana).
Prometheus query: `sum(rate(irandirect_routes_operations_failed_total[10m])) / sum(rate(irandirect_routes_operations_requested_total[10m]))`

## Symptoms
Runtime Reconciliation 'Route operation failures' panel rising; success ratio per operation dropping.

## Likely causes
Native route API errors; permission/state issues on the host routing table; concurrent external route changes; throttling.

## Safe checks
Open Runtime Reconciliation dashboard; split by operation (enumerate/create/delete) to find the failing kind; correlate with Tempo traces.

## Corrective actions
Identify the failing operation kind. If create/delete fail, the host routing state may be inconsistent - do not force repeated deletes. If enumerate fails, the read path is broken and reconciliation is blind.

## What not to do
Do not retry failing create/delete in a tight loop - that amplifies load and can wedge the host routing table. Do not edit routes directly to 'fix' while alerting.

## Escalation criteria
If failure ratio >30% or create/delete fully failing, escalate to the Service owner with the failing operation kind.

## Evidence to preserve
Per-operation success ratio, Tempo traces of failing calls, host routing state snapshot (if safe to capture).

## Resolution verification
Route operation failure ratio returns below 5%.

## Related alerts
IranDirectRuntimeCycleFailureRateHigh

## Ownership
Service / SRE
