# Runbook: (procedure, not an alert)

- **Alert:** `(procedure, not an alert)`
- **Severity:** n/a
- **Dashboard:** `pathveer-reliability-errors`
- **Prometheus query:** `n/a`

## What it means
Safe restart procedure for any observability backend without losing application telemetry truth.

## User impact
Lets operators recover a backend while keeping the others and the Service intact.

## Dashboard
Open the `pathveer-reliability-errors` dashboard in the PathVeer folder (Grafana).
Prometheus query: `n/a`

## Symptoms
A backend container is unhealthy but the Service and other backends are fine.

## Likely causes
n/a - operational procedure.

## Safe checks
Confirm the Service is still running and the collector is up before restarting a dependent backend.

## Corrective actions
Restart only the unhealthy backend: 'docker compose restart <service>'. Prometheus keeps 30d history; Tempo/Grafana/Alertmanager reprovision from config+volume on start. Never 'docker compose down' the whole stack to fix one service.

## What not to do
Do not 'docker compose down' to recover one service - that stops everything and drops in-flight state. Do not restart the Service unless it is the fault.

## Escalation criteria
If a restart does not recover the backend, follow its specific runbook and escalate.

## Evidence to preserve
'docker compose ps' before/after, the restarted service logs.

## Resolution verification
The restarted backend reports healthy and its data resumes.

## Related alerts
(all infrastructure runbooks)

## Ownership
Observability / Platform
