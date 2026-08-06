# Runbook: IranDirectPrefixChecksFailing

- **Alert:** `IranDirectPrefixChecksFailing`
- **Severity:** warning
- **Dashboard:** `irandirect-prefix-dns`
- **Prometheus query:** `sum(rate(irandirect_prefix_checks_total{outcome!="success"}[30m])) / sum(rate(irandirect_prefix_checks_total[30m]))`

## What it means
More than 20% of official prefix update checks failed over 30m. (The planned consecutive-failure gauge is not instrumented; this uses the check outcome rate.)

## User impact
The official prefix list may be stale or unreachable; desired-state checks against the prefix source can give wrong results.

## Dashboard
Open the `irandirect-prefix-dns` dashboard in the IranDirect folder (Grafana).
Prometheus query: `sum(rate(irandirect_prefix_checks_total{outcome!="success"}[30m])) / sum(rate(irandirect_prefix_checks_total[30m]))`

## Symptoms
Prefix/DNS dashboard 'Prefix check rate (by outcome)' showing failure share.

## Likely causes
Prefix source endpoint unreachable; HTTP 4xx/5xx; TLS/cert issue on the prefix source; network partition.

## Safe checks
Check the failure outcome on the dashboard. From a browser/host, manually reach the prefix source endpoint. Check for TLS errors in collector/Service logs (without exposing the URL in the alert).

## Corrective actions
Confirm whether the prefix source is genuinely down or returning errors. If transient, watch. If sustained, the prefix data is stale - coordinate with the prefix-source owner; do not override desired state based on stale data.

## What not to do
Do not hardcode or paste the prefix source URL into tickets/logs. Do not disable prefix checking to clear the alert.

## Escalation criteria
If the prefix source is confirmed down for >1h, escalate to the prefix-source owner; note desired-state may be based on stale prefix data.

## Evidence to preserve
Prefix-check outcome history, manual reachability result of the prefix source, relevant log lines (no URL/payload).

## Resolution verification
Prefix check failure ratio returns below 20%.

## Related alerts
IranDirectDnsLookupFailureRatioHigh

## Ownership
Service / Network
