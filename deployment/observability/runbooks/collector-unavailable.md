# Runbook: PathVeerCollectorUnavailable

- **Alert:** `PathVeerCollectorUnavailable`
- **Severity:** critical
- **Dashboard:** `pathveer-reliability-errors`
- **Prometheus query:** `up{job="otel-collector"} == 0`

## What it means
Prometheus cannot scrape the OpenTelemetry Collector. All application telemetry ingestion has stopped.

## User impact
No new metrics or traces are collected. Dashboards go stale and application alerts become unreliable (inhibition suppresses them while the collector is down).

## Dashboard
Open the `pathveer-reliability-errors` dashboard in the PathVeer folder (Grafana).
Prometheus query: `up{job="otel-collector"} == 0`

## Symptoms
Dashboards flatline; collector health check fails; 'connection refused' in collector logs.

## Likely causes
Collector container stopped/crashed; compose restart; OOM via memory_limiter misconfig; broken config mount; host Docker issue.

## Safe checks
docker compose ps; docker compose logs otel-collector --tail=50; curl -s http://localhost:13133/ (health).

## Corrective actions
Confirm container state. If stopped, 'docker compose restart otel-collector'. If config error, diff collector YAML vs HEAD and fix. Watch memory_limiter limits. Bring collector back before investigating app alerts.

## What not to do
Do not restart the PathVeer.Service to 'fix' telemetry - the Service is fine; the collector is the fault. Do not disable alerts.

## Escalation criteria
If collector will not stay up after one restart and memory_limiter is suspected, escalate to the platform owner with collector logs and the compose file.

## Evidence to preserve
Collector container logs, 'docker compose ps' output, last successful scrape timestamp from Prometheus Targets page.

## Resolution verification
up{job="otel-collector"} returns 1 and application metrics resume incrementing in Prometheus.

## Related alerts
PathVeerServiceTelemetryAbsent, PathVeerPrometheusTargetDown

## Ownership
Observability / Platform
