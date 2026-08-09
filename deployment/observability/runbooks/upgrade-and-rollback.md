# Runbook: (procedure, not an alert)

- **Alert:** `(procedure, not an alert)`
- **Severity:** n/a
- **Dashboard:** `pathveer-reliability-errors`
- **Prometheus query:** `n/a`

## What it means
How to upgrade or roll back a stack component image safely.

## User impact
Prevents a bad image bump from taking down observability.

## Dashboard
Open the `pathveer-reliability-errors` dashboard in the PathVeer folder (Grafana).
Prometheus query: `n/a`

## Symptoms
A component upgrade is planned or a freshly upgraded component is misbehaving.

## Likely causes
n/a - operational procedure.

## Safe checks
Read the component's changelog; confirm config compatibility with the target image; snapshot the relevant named volume if risky.

## Corrective actions
Bump the image tag in docker-compose.yml for ONE component, 'docker compose up -d <service>', verify health + scrape + dashboards. To roll back, restore the previous tag and restart. Keep upgrades one component at a time.

## What not to do
Do not bump all image tags at once. Do not edit application telemetry to 'match' a new backend. Do not wipe volumes during rollback unless data is corrupt.

## Escalation criteria
If a rollback does not restore function, escalate to the platform owner with the before/after image tags and logs.

## Evidence to preserve
Image tags before/after, health checks, scrape status, any volume snapshot taken.

## Resolution verification
Upgraded component is healthy and all dependent data flows resume.

## Related alerts
(all infrastructure runbooks)

## Ownership
Observability / Platform
