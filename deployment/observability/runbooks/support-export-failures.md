# Runbook: PathVeerSupportExportFailures

- **Alert:** `PathVeerSupportExportFailures`
- **Severity:** warning
- **Dashboard:** `pathveer-ipc-support`
- **Prometheus query:** `sum(increase(pathveer_support_bundles_failed_total[30m])) >= 2`

## What it means
At least two support bundle exports failed in 30m.

## User impact
Diagnostics collection is unreliable; remote assistance and post-incident analysis are impaired.

## Dashboard
Open the `pathveer-ipc-support` dashboard in the PathVeer folder (Grafana).
Prometheus query: `sum(increase(pathveer_support_bundles_failed_total[30m])) >= 2`

## Symptoms
IPC/Support dashboard 'Support exports over time' showing failed series; 'Support export failure ratio' elevated.

## Likely causes
Output location unavailable; serialization/IO error; insufficient permissions at the export target; concurrent export conflict.

## Safe checks
Check the support-export failure ratio on the dashboard. Confirm the export target location is reachable and writable from the Service account (without exposing the path in the alert).

## Corrective actions
Identify the failure stage from the dashboard (per-operation p95). Restore the export target access. Retry the export manually once access is confirmed.

## What not to do
Do not expose or paste the export output path into tickets. Do not disable support export.

## Escalation criteria
If exports fail repeatedly with access errors, escalate to the Service owner with the failure stage and the (redacted) access symptom.

## Evidence to preserve
Support-export failure ratio, failure stage from dashboard, access symptom (no path/payload).

## Resolution verification
Support bundle exports succeed; failed counter stops increasing.

## Related alerts
PathVeerIpcTimeoutRatioHigh

## Ownership
Service / Support
