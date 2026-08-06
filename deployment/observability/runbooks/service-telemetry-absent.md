# Runbook: IranDirectServiceTelemetryAbsent

- **Alert:** `IranDirectServiceTelemetryAbsent`
- **Severity:** warning
- **Dashboard:** `irandirect-service-overview`
- **Prometheus query:** `sum(irandirect:runtime_cycle_rate5m) < 0.0001 and max(timestamp(irandirect_runtime_cycles_started_total)) < time() - 900`

## What it means
No runtime reconciliation cycles have started for over 15 minutes despite earlier telemetry.

## User impact
The Service may be stopped, disabled, or failing before the cycle begins. Routes will not be reconciled to desired state.

## Dashboard
Open the `irandirect-service-overview` dashboard in the IranDirect folder (Grafana).
Prometheus query: `sum(irandirect:runtime_cycle_rate5m) < 0.0001 and max(timestamp(irandirect_runtime_cycles_started_total)) < time() - 900`

## Symptoms
Service Overview 'Telemetry last seen' grows; cycle rate panel flat at zero.

## Likely causes
Service process stopped; Observability disabled in config; Service crashing at startup; collector down (check that first - see collector runbook).

## Safe checks
Is the IranDirect.Service process running (Services console)? Is Observability:Enabled true? Check Service event log for startup faults. Confirm collector is up.

## Corrective actions
If the Service should be running: start it / check why it exited. If observability was intentionally disabled, silence the alert (document why). If crashing at startup, pull the Service log and triage the startup fault.

## What not to do
Do not re-enable telemetry blindly if the Service is intentionally offline. Do not treat as a collector problem without first confirming up{job="otel-collector"}=1.

## Escalation criteria
If the Service is up but emits nothing and is not crashing, escalate to the Service owner with the last Service log and the observability config.

## Evidence to preserve
Service process state, Observability config block, Service application log, last cycle timestamp.

## Resolution verification
Cycle rate returns above zero and 'Telemetry last seen' resets to seconds.

## Related alerts
IranDirectCollectorUnavailable, IranDirectRuntimeCycleFailureRateHigh

## Ownership
Service / SRE
