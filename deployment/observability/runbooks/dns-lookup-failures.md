# Runbook: PathVeerDnsLookupFailureRatioHigh

- **Alert:** `PathVeerDnsLookupFailureRatioHigh`
- **Severity:** warning
- **Dashboard:** `pathveer-prefix-dns`
- **Prometheus query:** `sum(rate(pathveer_dns_lookups_total{outcome!="success"}[15m])) / sum(rate(pathveer_dns_lookups_total[15m]))`

## What it means
More than 20% of custom-route DNS lookups failed over 15m.

## User impact
Custom-route resolution is partially failing; some custom routes may not be correctly evaluated.

## Dashboard
Open the `pathveer-prefix-dns` dashboard in the PathVeer folder (Grafana).
Prometheus query: `sum(rate(pathveer_dns_lookups_total{outcome!="success"}[15m])) / sum(rate(pathveer_dns_lookups_total[15m]))`

## Symptoms
Prefix/DNS dashboard 'DNS lookup rate (by outcome)' showing failure share.

## Likely causes
Configured DNS resolver unreachable; resolver returning SERVFAIL; custom-route names unresolvable; network partition.

## Safe checks
Check the failure outcome on the dashboard. Confirm the configured resolver is reachable from the host. Verify a sample custom-route name resolves.

## Corrective actions
Identify whether the resolver or the names are at fault. If the resolver is down, restore it. If specific names are unresolvable, that is a desired-state data problem - route to the custom-route owner.

## What not to do
Do not cache or pin resolved addresses to silence the alert. Do not expose the resolver address or query names in tickets.

## Escalation criteria
If the resolver is confirmed down, escalate to the DNS/platform owner.

## Evidence to preserve
DNS outcome history, resolver reachability result, sample resolution result (no names beyond what is already in desired state).

## Resolution verification
DNS lookup failure ratio returns below 20%.

## Related alerts
PathVeerPrefixChecksFailing

## Ownership
Service / Network
