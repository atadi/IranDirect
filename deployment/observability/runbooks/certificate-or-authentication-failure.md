# Runbook: IranDirectCollectCertificateOrAuthFailure

- **Alert:** `IranDirectCollectCertificateOrAuthFailure`
- **Severity:** warning
- **Dashboard:** `irandirect-reliability-errors`
- **Prometheus query:** `sum(rate(irandirect_runtime_cycles_started_total[15m])) < 0.0001 and max(timestamp(...)) > time()-300 and up{job="otel-collector"}==1`

## What it means
The collector is up but the Service stopped emitting cycles while recent telemetry exists - a possible OTLP auth/cert failure if collector auth is enabled.

## User impact
If OTLP auth is enabled, telemetry export is being rejected; dashboards go stale and app alerts become unreliable.

## Dashboard
Open the `irandirect-reliability-errors` dashboard in the IranDirect folder (Grafana).
Prometheus query: `sum(rate(irandirect_runtime_cycles_started_total[15m])) < 0.0001 and max(timestamp(...)) > time()-300 and up{job="otel-collector"}==1`

## Symptoms
Telemetry last seen grows while collector health is green; no new application series in Prometheus.

## Likely causes
Expired client certificate; rotated/invalid OTLP token; collector auth config change; Service OTLP header env misconfig.

## Safe checks
Confirm whether collector OTLP auth is enabled. If yes, check collector logs for 401/403 on the OTLP endpoint and the Service OTLP header env var name. If auth is disabled, this alert is likely a Service-side stop - see service-telemetry-absent.

## Corrective actions
If auth is enabled and rejecting: renew the client cert/token, update the Service OTLP header env (name only), restart the Service. If auth is disabled, treat as a Service stop, not an auth failure.

## What not to do
Do not disable collector auth to 'fix' telemetry. Do not paste tokens/certs into tickets - reference the env var name only.

## Escalation criteria
If auth cannot be restored, escalate to the platform owner with the collector 401/403 log lines (no token/cert values).

## Evidence to preserve
Collector OTLP 401/403 log lines (no token/cert), Service OTLP env var name, telemetry last-seen.

## Resolution verification
Application series resume incrementing in Prometheus.

## Related alerts
IranDirectCollectorUnavailable, IranDirectServiceTelemetryAbsent

## Ownership
Observability / Platform
