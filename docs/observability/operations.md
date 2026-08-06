# Observability Operations Guide

How to operate the optional OpenTelemetry export layer in IranDirect.Service.

## Disabled by default

Telemetry export is **off** unless you explicitly enable it:

```json
"Observability": { "Enabled": false, ... }
```

With `Enabled = false` the Service starts exactly as before: no
`TracerProvider`/`MeterProvider`, no exporter threads, no network connections.
All Phase 32.1–32.8 in-process telemetry (ActivitySource/Meter) still exists
but is not exported anywhere.

## Configuration keys

| Key | Default | Meaning |
|-----|---------|---------|
| `Observability:Enabled` | `false` | Master switch for export hosting. |
| `Observability:TracingEnabled` | `true` | Register tracing when enabled. |
| `Observability:MetricsEnabled` | `true` | Register metrics when enabled. |
| `Observability:SamplingRatio` | `1.0` | Root span sampling ratio `0.0`–`1.0`. |
| `Observability:ExportTimeoutSeconds` | `5` | OTLP per-export timeout (1–60). |
| `Observability:ShutdownFlushTimeoutSeconds` | `5` | Bounded flush window on shutdown (1–30). |
| `Observability:ServiceName` | `IranDirect.Service` | `service.name` resource attribute. |
| `Observability:Environment` | `""` | `deployment.environment.name` (falls back to host env name). |
| `Observability:Otlp:Enabled` | `false` | OTLP export switch. |
| `Observability:Otlp:Endpoint` | `""` | Absolute `http(s)` collector URL. |
| `Observability:Otlp:Protocol` | `grpc` | `grpc` or `http/protobuf`. |
| `Observability:Otlp:HeadersEnvironmentVariable` | `""` | Name of env var holding OTLP headers. |
| `Observability:Console:Enabled` | `false` | Console export switch (Development only). |

## OTLP setup

1. Set `Observability:Enabled = true` and `Observability:Otlp:Enabled = true`.
2. Provide a non-empty absolute endpoint, e.g.
   `Observability:Otlp:Endpoint = "http://localhost:4317"` (gRPC) or
   `https://otlp.collector.example:4318` (HTTP/protobuf).
3. Choose `Protocol`: `grpc` or `http/protobuf`.

Example (development, gRPC to a local collector):

```json
"Observability": {
  "Enabled": true,
  "Otlp": {
    "Enabled": true,
    "Endpoint": "http://localhost:4317",
    "Protocol": "grpc"
  }
}
```

## Environment-variable secret handling

Never put OTLP auth headers in `appsettings`. Instead, name the environment
variable that holds the header string:

```json
"Observability": {
  "Enabled": true,
  "Otlp": {
    "Enabled": true,
    "Endpoint": "https://otlp.collector.example:4318",
    "Protocol": "http/protobuf",
    "HeadersEnvironmentVariable": "IRANDIRECT_OTLP_HEADERS"
  }
}
```

Then supply the value out-of-band (CI secret, process environment):

```powershell
$env:IRANDIRECT_OTLP_HEADERS = "Authorization=Bearer <token>"
```

- The header **value** never appears in configuration, logs, or exception
  messages.
- If `HeadersEnvironmentVariable` is set but the variable is missing/empty,
  configuration validation fails at startup with a concise error.
- If `HeadersEnvironmentVariable` is blank, no headers are sent (no failure).

## Console-export development restriction

The console exporter is for local development/debugging only:

- Allowed **only** when the host environment is `Development` **and**
  `Observability:Console:Enabled = true`.
- Enabling it outside `Development` fails startup with a configuration error.
- It is never enabled merely because OTLP is disabled.

Example (Development only):

```json
// appsettings.Development.json
"Observability": {
  "Enabled": true,
  "Console": { "Enabled": true }
}
```

## Service and resource attributes

Reported resource attributes (bounded, non-sensitive):

- `service.name` = `IranDirect.Service` (configurable).
- `service.version` = Core assembly informational version.
- `deployment.environment.name` = configured environment or host env name.

Not reported (privacy policy): `service.instance.id`, `machine.name`,
`host.name`, `user.name`, `process.command_line`, local IP, installation path,
organization identifiers.

## Sampling

`SamplingRatio` uses parent-based ratio sampling:

- `1.0` — sample all root spans.
- `0.0` — sample no root spans (children of an already-sampled parent are still
  recorded).
- Values between — probabilistic root sampling.

Trace sampling affects spans only; **metrics are not affected** by trace
sampling.

## Collector-unavailable behavior

If the OTLP collector is down or rejects exports:

- Exporter errors are isolated by the OpenTelemetry SDK and logged; they do
  **not** propagate into Core business calls (route reconciliation, IPC, support
  export continue unaffected).
- Export uses a bounded queue/backpressure (SDK defaults); it cannot block
  runtime work indefinitely.
- The Service starts and runs normally even if no exporter is reachable.

## Shutdown / flush

- On Windows Service stop (or host shutdown), providers are disposed by the
  framework hosting layer within the bounded `ShutdownFlushTimeoutSeconds`
  window.
- No custom infinite wait; telemetry shutdown failures do not prevent process
  termination.
- Do **not** call `ForceFlush` on runtime cycles or requests.

## Privacy guarantees

- No URLs, domains, IPs, prefixes, file paths, payloads, machine/user names, or
  exception messages are attached to telemetry (enforced by Phases 32.1–32.8
  tag catalog and architecture tests).
- No sensitive resource attributes (see above).
- Secrets are never written to committed configuration.

## Troubleshooting

| Symptom | Cause | Fix |
|---------|-------|-----|
| Service fails to start after enabling | Invalid config (bad endpoint, console outside Dev, bad sampling/timeout) | Read the startup `ArgumentException` message; correct `appsettings`. |
| No spans/metrics arrive at collector | OTLP disabled, wrong endpoint, or collector unreachable | Verify `Otlp.Enabled`/`Endpoint`/`Protocol`; check collector reachability. |
| Auth rejected by collector | Missing/invalid OTLP headers | Set `Otlp.HeadersEnvironmentVariable` to a variable that contains the header string. |
| Console exporter didn't start | Not in `Development` or `Console.Enabled=false` | Enable only in `Development` with `Console.Enabled=true`. |
| High export latency | Collector slow | Raise `ExportTimeoutSeconds` within bounds; ensure collector capacity. |

## Example environment-variable configuration (PowerShell)

```powershell
# Development: console + local OTLP, headers from env
$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:IRANDIRECT_OTLP_HEADERS = "Authorization=Bearer <dev-token>"
# appsettings.Development.json:
#   "Observability": { "Enabled": true,
#     "Otlp": { "Enabled": true, "Endpoint": "http://localhost:4317",
#               "HeadersEnvironmentVariable": "IRANDIRECT_OTLP_HEADERS" },
#     "Console": { "Enabled": true } }
```

## Alerting (Phase 33.4)

The consumption stack evaluates alerts in **Prometheus** (group
`irandirect_alerts`, 15 rules) and routes them through **Alertmanager** (fifth
stack service). Alert *truth* lives in Prometheus; Grafana only links to alerts
and runbooks and does not manage them.

- **Local default is no-op.** Every alert (all severities) routes to the built-in
  `null` receiver, which delivers nothing. Alerts are inspected in the
  Alertmanager UI/API (`http://localhost:9095`, loopback only) or Prometheus →
  Alerts. The stack never exfiltrates data or pages anyone locally.
- **Severity model:** `info` (UI only), `warning` (future business-hours
  notification), `critical` (future immediate notification). Grouping is by
  `alertname` / `deployment_environment_name` / `severity`; timings are
  `group_wait 30s`, `group_interval 5m`, `repeat_interval 4h` (critical 1h).
- **Inhibition:** the collector being down suppresses service-component
  telemetry-absence alerts (telemetry is then unreliable); a down target
  suppresses its derivatives; critical variants suppress matching warning
  variants. `AlertmanagerUnavailable` does **not** silence application alerts in
  Prometheus (alerts still evaluate; only delivery stops).
- **Secret-bearing production delivery** (SMTP/email, webhook, PagerDuty) is
  deferred to Phase 33.5. `.env.example` lists the `ALERTMANAGER_*` placeholder
  keys; no values are committed.

### Runbook catalog

Every alert links to `deployment/observability/runbooks/<name>.md`:

| Alert | Runbook |
|-------|---------|
| `IranDirectServiceTelemetryAbsent` | `service-telemetry-absent.md` |
| `IranDirectRuntimeCycleFailureRateHigh` / `…Critical` | `runtime-cycle-failures.md` |
| `IranDirectRuntimeCycleLatencyHigh` | `runtime-cycle-latency.md` |
| `IranDirectRouteOperationFailureRatioHigh` | `route-operation-failures.md` |
| `IranDirectPrefixChecksFailing` | `prefix-check-failures.md` |
| `IranDirectDnsLookupFailureRatioHigh` | `dns-lookup-failures.md` |
| `IranDirectIpcTimeoutRatioHigh` | `ipc-timeouts.md` |
| `IranDirectSupportExportFailures` | `support-export-failures.md` |
| `IranDirectCollectorUnavailable` | `collector-unavailable.md` |
| `IranDirectPrometheusTargetDown` | `prometheus-target-down.md` |
| `IranDirectAlertmanagerUnavailable` | `alertmanager-unavailable.md` |
| `IranDirectTempoUnavailable` | `tempo-unavailable.md` |
| `IranDirectGrafanaUnavailable` | `grafana-unavailable.md` |
| `IranDirectCollectCertificateOrAuthFailure` | `certificate-or-authentication-failure.md` |

Procedure runbooks: `safe-restart.md`, `upgrade-and-rollback.md`. Coverage gap
(documented, not alerted): `prometheus-storage-pressure.md` — no host/container
disk metric is available without node-exporter (Phase 33.5).

### Inspect / silence

```bash
curl -s 'http://localhost:9090/api/v1/alerts'        # active alerts (Prometheus)
curl -s 'http://localhost:9095/api/v2/alerts'        # active alerts (Alertmanager)
docker compose exec -T alertmanager amtool config show   # 3 inhibit rules
```

Safe silence (do **not** disable the rule): open the firing alert in the
Alertmanager UI → Silence → comment + duration → Create.

## Explicit non-goals

- No automatic runtime/process/HTTP instrumentation.
- No telemetry added to CLI/Tray business logic.
- No change to Activity names, metric names, tag catalogs, or IPC wire format.
- No vendor-specific or custom exporters.
