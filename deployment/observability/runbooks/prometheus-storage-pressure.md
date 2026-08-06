# Runbook: (planned - deferred; see note)

- **Alert:** `(planned - deferred; see note)`
- **Severity:** n/a
- **Dashboard:** `irandirect-reliability-errors`
- **Prometheus query:** `(no alert in Phase 33.4 - requires node-exporter or infra disk metrics)`

## What it means
Prometheus storage/filesystem pressure is NOT alerted in Phase 33.4.

## User impact
Without node-exporter or container filesystem metrics, host/container disk pressure on the Prometheus volume cannot be detected here.

## Dashboard
Open the `irandirect-reliability-errors` dashboard in the IranDirect folder (Grafana).
Prometheus query: `(no alert in Phase 33.4 - requires node-exporter or infra disk metrics)`

## Symptoms
Prometheus TSDB may grow toward the 30d retention cap; container disk fills silently.

## Likely causes
n/a - this is a coverage gap, not an incident.

## Safe checks
Watch 'docker system df' and the irandirect-prometheus-data volume size. Plan node-exporter or infra monitoring for Phase 33.5.

## Corrective actions
If the volume is near full, raise retention shorter or expand the volume. Treat as capacity planning, not paging.

## What not to do
Do not add node-exporter in this phase (out of scope per the phase brief). Do not fabricate a storage metric.

## Escalation criteria
If disk is critically low, escalate to the platform owner to expand the volume before Prometheus stops ingesting.

## Evidence to preserve
Volume size, retention setting, 'docker system df'.

## Resolution verification
Volume has headroom; capacity plan in place for 33.5.

## Related alerts
IranDirectPrometheusTargetDown

## Ownership
Observability / Platform
