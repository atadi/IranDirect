# Runbook: IranDirectPrometheusTargetDown

- **Alert:** `IranDirectPrometheusTargetDown`
- **Severity:** warning
- **Dashboard:** `irandirect-reliability-errors`
- **Prometheus query:** `up{job=~"tempo|grafana|alertmanager"} == 0`

## What it means
One of the tempo/grafana/alertmanager scrape targets has been unreachable for 2m.

## User impact
The down backend's data/dashboards are incomplete; for alertmanager, alert delivery stops (evaluation continues).

## Dashboard
Open the `irandirect-reliability-errors` dashboard in the IranDirect folder (Grafana).
Prometheus query: `up{job=~"tempo|grafana|alertmanager"} == 0`

## Symptoms
Reliability dashboard 'Scrape target availability' showing a target at 0.

## Likely causes
Backend container stopped; compose restart; port/host change; resource exhaustion.

## Safe checks
docker compose ps; docker compose logs <service> --tail=30; check the specific target on Prometheus Targets.

## Corrective actions
Bring the affected backend back with 'docker compose restart <service>'. For alertmanager, also confirm alerts resume delivery after recovery.

## What not to do
Do not restart Prometheus to fix a backend target. Do not disable the alert.

## Escalation criteria
If a backend will not stay up, escalate to the platform owner with its logs.

## Evidence to preserve
Affected backend logs, 'docker compose ps', Prometheus Targets page.

## Resolution verification
up for the target returns 1 and its data resumes.

## Related alerts
IranDirectAlertmanagerUnavailable, IranDirectTempoUnavailable, IranDirectGrafanaUnavailable

## Ownership
Observability / Platform
