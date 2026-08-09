# Runbook: PathVeerGrafanaUnavailable

- **Alert:** `PathVeerGrafanaUnavailable`
- **Severity:** warning
- **Dashboard:** `pathveer-reliability-errors`
- **Prometheus query:** `up{job="grafana"} == 0`

## What it means
Prometheus cannot scrape Grafana for 2m.

## User impact
Dashboards and the Alertmanager UI entry point are unavailable; metrics, traces, and alert evaluation continue unaffected.

## Dashboard
Open the `pathveer-reliability-errors` dashboard in the PathVeer folder (Grafana).
Prometheus query: `up{job="grafana"} == 0`

## Symptoms
Reliability dashboard scrape availability shows grafana at 0; http://localhost:3000 unreachable.

## Likely causes
Grafana container stopped; compose restart; auth/env misconfig; volume/permission issue.

## Safe checks
docker compose ps; docker compose logs grafana --tail=30; confirm .env has GF_SECURITY_ADMIN_USER/PASSWORD set.

## Corrective actions
Restart Grafana with 'docker compose restart grafana'. If auth env is missing, restore it in .env (never commit it). Dashboards reprovision from JSON on start.

## What not to do
Do not change Grafana auth settings to 'fix' availability. Do not disable the alert.

## Escalation criteria
If Grafana will not start due to auth/env errors, escalate to the platform owner.

## Evidence to preserve
Grafana logs, .env presence (no values), 'docker compose ps'.

## Resolution verification
up{job="grafana"} returns 1 and http://localhost:3000 responds.

## Related alerts
PathVeerPrometheusTargetDown

## Ownership
Observability / Platform
