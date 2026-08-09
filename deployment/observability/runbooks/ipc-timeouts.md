# Runbook: PathVeerIpcTimeoutRatioHigh

- **Alert:** `PathVeerIpcTimeoutRatioHigh`
- **Severity:** warning
- **Dashboard:** `pathveer-ipc-support`
- **Prometheus query:** `sum(rate(pathveer_ipc_requests_total{outcome="timeout"}[10m])) / sum(rate(pathveer_ipc_requests_total[10m]))`

## What it means
More than 5% of named-pipe IPC requests timed out over 10m.

## User impact
CLI/Tray commands to the Service are partially failing to get a response; operators cannot drive the Service.

## Dashboard
Open the `pathveer-ipc-support` dashboard in the PathVeer folder (Grafana).
Prometheus query: `sum(rate(pathveer_ipc_requests_total{outcome="timeout"}[10m])) / sum(rate(pathveer_ipc_requests_total[10m]))`

## Symptoms
IPC/Support dashboard 'IPC timeout and failure rate' rising; 'IPC timeout ratio' stat elevated.

## Likely causes
Service busy/blocked (long reconcile holding the pipe); pipe instance exhaustion; client (CLI/Tray) bug; host CPU saturation.

## Safe checks
Check IPC dashboard for timeout vs other outcomes. Check Service host CPU. Confirm a single client is not spamming requests.

## Corrective actions
If the Service is mid-long-reconcile, timeouts are transient - wait for reconcile to finish. If sustained, investigate what is blocking the Service main loop. Restart the offending client, not the Service, if a client is misbehaving.

## What not to do
Do not kill the Service to 'fix' IPC - that drops in-flight reconciliation. Do not expose ipc_command payloads.

## Escalation criteria
If timeouts are sustained with normal load, escalate to the Service owner with the IPC dashboard capture.

## Evidence to preserve
IPC outcome history, Service host CPU at the time, client identity if known (no payload).

## Resolution verification
IPC timeout ratio returns below 5%.

## Related alerts
PathVeerSupportExportFailures

## Ownership
Service / SRE
