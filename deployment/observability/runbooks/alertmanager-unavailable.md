# Runbook: PathVeerAlertmanagerUnavailable

- **Alert:** `PathVeerAlertmanagerUnavailable`
- **Severity:** warning
- **Dashboard:** `pathveer-reliability-errors`
- **Prometheus query:** `up{job="alertmanager"} == 0`

## What it means
Prometheus cannot scrape Alertmanager for 2m.

## User impact
Alerts are still evaluated by Prometheus but cannot be delivered. Notification delivery is interrupted until recovery.

## Dashboard
Open the `pathveer-reliability-errors` dashboard in the PathVeer folder (Grafana).
Prometheus query: `up{job="alertmanager"} == 0`

## Symptoms
Reliability dashboard scrape availability shows alertmanager at 0; http://localhost:9093 unreachable.

## Likely causes
Alertmanager container stopped; compose restart; volume/permission issue.

## Safe checks
docker compose ps; docker compose logs alertmanager --tail=30; curl -s http://localhost:9093/-/healthy.

## Corrective actions
Restart Alertmanager with 'docker compose restart alertmanager'. After recovery, confirm alerts resume appearing in the Alertmanager UI.

## What not to do
Do not disable Prometheus alerting because Alertmanager is down. Do not treat missing notifications as 'no alerts'.

## Escalation criteria
If Alertmanager will not stay up, escalate to the platform owner with its logs - and watch Prometheus alerts manually in the interim.

## Evidence to preserve
Alertmanager logs, 'docker compose ps', last successful scrape timestamp.

## Resolution verification
up{job="alertmanager"} returns 1 and the Alertmanager UI lists active alerts.

## Related alerts
PathVeerPrometheusTargetDown

## Ownership
Observability / Platform
