# Runbook: PathVeerTempoUnavailable

- **Alert:** `PathVeerTempoUnavailable`
- **Severity:** warning
- **Dashboard:** `pathveer-reliability-errors`
- **Prometheus query:** `up{job="tempo"} == 0`

## What it means
Prometheus cannot scrape Tempo for 2m.

## User impact
Distributed traces are not queryable in Grafana; metrics and reconciliation are unaffected.

## Dashboard
Open the `pathveer-reliability-errors` dashboard in the PathVeer folder (Grafana).
Prometheus query: `up{job="tempo"} == 0`

## Symptoms
Reliability dashboard scrape availability shows tempo at 0; Tempo Explore returns no results.

## Likely causes
Tempo container stopped; compose restart; volume/permission issue; resource exhaustion.

## Safe checks
docker compose ps; docker compose logs tempo --tail=30; curl -s http://localhost:3200/ready.

## Corrective actions
Restart Tempo with 'docker compose restart tempo'. Trace history on the volume survives unless the volume was wiped.

## What not to do
Do not restart the Service because traces are missing. Do not wipe the tempo volume to 'fix' it.

## Escalation criteria
If Tempo will not stay up, escalate to the platform owner with its logs.

## Evidence to preserve
Tempo logs, 'docker compose ps', last successful scrape timestamp.

## Resolution verification
up{job="tempo"} returns 1 and Tempo Explore returns traces.

## Related alerts
PathVeerPrometheusTargetDown

## Ownership
Observability / Platform
