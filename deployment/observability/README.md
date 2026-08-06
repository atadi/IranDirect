# IranDirect local observability stack

Reproducible local development stack that receives the telemetry
`IranDirect.Service` already emits (Phases 32.2–32.9). It adds **no**
instrumentation: no new spans, no new metrics, no renamed Activity/Meter/tag.
The stack is purely a consumer.

Implements the reference architecture selected in
[Phase 33.1](../../docs/observability/phase-33.1-telemetry-consumption-architecture.md)
(Option A).

## 1. Stack

| Component | Image | Role |
|-----------|-------|------|
| `otel-collector` | `otel/opentelemetry-collector-contrib:0.115.1` | The **only** OTLP endpoint the Service talks to. Receives OTLP, batches, re-exports. |
| `prometheus` | `prom/prometheus:v3.0.1` | Metric storage. Scrapes the collector every 15s, 30-day retention. |
| `tempo` | `grafana/tempo:2.6.1` | Trace storage. Filesystem backend, local blocks, 3-day dev retention. |
| `grafana` | `grafana/grafana:11.4.0` | Dashboarding. Prometheus + Tempo datasources provisioned; five version-controlled dashboards auto-loaded into the `IranDirect` folder (Phase 33.3). |

Deliberately excluded: Loki, Alloy, Jaeger, Zipkin, Elasticsearch, ClickHouse.
No log pipeline exists because the application emits no OTel logs.

The Service never addresses Prometheus, Tempo or Grafana directly. Only the
collector is application-facing.

## 2. Ports

All host ports bind to `127.0.0.1` only — nothing is exposed to the LAN.

| Host port | Container | Why it is published |
|-----------|-----------|---------------------|
| `4317` | otel-collector | OTLP gRPC ingest. The Windows Service runs on the host, outside the compose network. |
| `4318` | otel-collector | OTLP HTTP/protobuf ingest (alternative protocol). |
| `13133` | otel-collector | `health_check` extension, used by operator smoke tests. |
| `9090` | prometheus | PromQL UI/API for development queries. |
| `3000` | grafana | Operator UI. |

Not published (internal network only):

| Endpoint | Reason |
|----------|--------|
| `otel-collector:8889` | Prometheus scrape target; only Prometheus needs it. |
| `tempo:4317` | Trace ingest; only the collector writes to Tempo. |
| `tempo:3200` | Tempo query API; only Grafana reads it. |

## 3. Startup

```bash
cd deployment/observability
cp .env.example .env          # then edit: set Grafana admin user/password
docker compose up -d --wait   # waits for all four health checks
docker compose ps
```

`.env` is git-ignored. Compose fails fast if `GF_SECURITY_ADMIN_USER` or
`GF_SECURITY_ADMIN_PASSWORD` is unset.

Grafana: <http://localhost:3000> (anonymous access disabled; sign in with the
`.env` credentials).

## 4. Shutdown

```bash
docker compose stop      # stop containers, keep them and all data
docker compose down      # remove containers and network, KEEP named volumes
```

Every service uses `restart: unless-stopped`, so containers come back after a
Docker/host restart unless you explicitly stopped them.

## 5. Volumes

| Volume | Mounted at | Contents |
|--------|-----------|----------|
| `irandirect-prometheus-data` | `/prometheus` | TSDB blocks + WAL (30-day retention) |
| `irandirect-tempo-data` | `/var/tempo` | Tempo WAL + trace blocks (3-day dev retention) |
| `irandirect-grafana-data` | `/var/lib/grafana` | Grafana SQLite: users, preferences, saved views |

Configuration is bind-mounted read-only from this directory and is
version-controlled; only the volumes above hold state.

## 6. Health checks

Every service defines a compose health check; `docker compose up -d --wait`
blocks until all report healthy.

Manual verification:

```bash
curl -s http://localhost:13133/            # collector: HTTP 200 when healthy
curl -s http://localhost:9090/-/healthy    # Prometheus: "Prometheus Server is Healthy."
curl -s http://localhost:3000/api/health   # Grafana: {"database":"ok",...}
docker compose exec tempo wget -qO- http://localhost:3200/ready   # Tempo: "ready"
```

Prometheus target health (the collector must be `up`):

```bash
curl -s 'http://localhost:9090/api/v1/query?query=up' 
```

## 7. Expected telemetry flow

```
IranDirect.Service (Windows host)
    │  OTLP gRPC → 127.0.0.1:4317   (only when Observability is enabled)
    ▼
otel-collector
    ├─ memory_limiter → batch → resource
    ├─ metrics → prometheus exporter :8889  ──scraped every 15s──▶ prometheus
    └─ traces  → otlp exporter → tempo:4317 ─────────────────────▶ tempo
                                                                     │
                                                     grafana ◀───────┘
                                                        └──▶ prometheus
```

What you should see once the Service runs with telemetry enabled:

- Metrics named `irandirect_*` in Prometheus (the OTel `irandirect.*` names are
  translated to `_` by the Prometheus exporter), e.g.
  `irandirect_runtime_cycles_started`, `irandirect_runtime_cycle_duration_bucket`.
- Traces in Tempo with `service.name = IranDirect.Service` and root spans
  `IranDirect.RuntimeCycle`, `IranDirect.PrefixUpdateCheck`,
  `IranDirect.CustomRouteRefresh`, `IranDirect.IpcRequest`,
  `IranDirect.SupportBundleExport`, `Ipc.Dispatch`.

Nothing appears until telemetry is explicitly enabled — that is by design.

## 8. Dashboards and recording rules (Phase 33.3)

Five dashboards are version-controlled and provisioned automatically — no manual
import. They live in `grafana/dashboards/*.json` and are mounted read-only into
the container at `/var/lib/grafana/dashboards`; the provider in
`grafana/provisioning/dashboards/dashboards.yaml` loads them into the
`IranDirect` folder with `allowUiUpdates: false` (edit the JSON, not the UI).

| UID | Title |
|-----|-------|
| `irandirect-service-overview` | IranDirect / Service Overview |
| `irandirect-runtime-reconciliation` | IranDirect / Runtime Reconciliation |
| `irandirect-prefix-dns` | IranDirect / Prefix and DNS |
| `irandirect-ipc-support` | IranDirect / IPC and Support Export |
| `irandirect-reliability-errors` | IranDirect / Reliability and Errors |

Recording rules live in `prometheus/rules/irandirect-recording-rules.yml`, one
group `irandirect_recording` (30s). Prometheus loads them via `rule_files` in
`prometheus.yml`, mounted read-only. The dashboards filter on
`deployment_environment_name` via an `$env` variable.

Validate after any change:

```bash
docker compose exec -T prometheus promtool check config /etc/prometheus/prometheus.yml
docker compose exec -T prometheus promtool check rules /etc/prometheus/rules/irandirect-recording-rules.yml
```

Inspect loaded rules: `curl -s 'http://localhost:9090/api/v1/rules'`.
Inspect a dashboard: Grafana → Dashboards → IranDirect folder.

The dashboards reference only metric names and labels verified against the live
stack. Some panels (IPC, prefix/DNS, cancellation) stay **empty** until those
code paths run; they are valid-but-quiet, not broken. Several contract metrics
(`service.enabled`, the prefix/DNS/route-inventory gauges, the repair counters,
`runtime.observe.duration`, `failure_category` label, …) are not emitted by the
running Service and are intentionally absent from every panel — see
[the Phase 33.3 doc](../../docs/observability/phase-33.3-dashboards-and-recording-rules.md).

## 9. Enabling observability in IranDirect.Service

Telemetry is **disabled by default and stays that way**. Nothing in this stack
turns it on. To enable it for a local session, override the `Observability`
section — for example via environment variables (no file edit, no risk of
committing an enabled config):

```bash
setx ASPNETCORE_ENVIRONMENT Development
set Observability__Enabled=true
set Observability__Otlp__Enabled=true
set Observability__Otlp__Endpoint=http://localhost:4317
set Observability__Otlp__Protocol=grpc
```

Or copy `IranDirect.Service/appsettings.Observability.Local.example.json` over
your local `appsettings.Development.json` (it is an **example** file and is not
loaded automatically).

The local stack requires no OTLP auth, so leave
`Observability:Otlp:HeadersEnvironmentVariable` empty. If you enable an
authenticator on the collector, point that setting at the *name* of an
environment variable holding the header value (e.g. `OTLP_HEADERS`) — the value
itself is never stored in configuration.

Sampling: `Observability:SamplingRatio` defaults to `1.0`, which is the
recommended development value (full fidelity).

## 10. Disabling observability

Remove the overrides, or set:

```
Observability__Enabled=false
```

With `Enabled=false` no tracer or meter provider is registered at all — the
Service behaves exactly as it does today, and the stack simply receives
nothing. This is the default in both `appsettings.json` and
`appsettings.Development.json`.

Stopping the stack while telemetry is enabled is also safe: the OTLP exporter
fails in isolation (bounded retry, then drop) and never affects routing,
reconciliation or IPC (Phase 32.9 failure isolation).

## 11. Wiping data

```bash
docker compose down -v                    # removes containers AND all volumes
```

Selective wipe:

```bash
docker compose down
docker volume rm irandirect-prometheus-data   # metrics only
docker volume rm irandirect-tempo-data        # traces only
docker volume rm irandirect-grafana-data      # Grafana users/prefs only
```

Note that wiping `irandirect-grafana-data` resets the admin account to the
values in `.env` on next start.

## 12. Manual verification procedure

The repository has no infrastructure test project, and deployment YAML is not
exercised by `dotnet test`. Verify this stack manually:

1. `docker compose up -d --wait` — all four services report healthy.
2. `curl http://localhost:13133/` — collector health returns 200.
3. `curl http://localhost:9090/-/healthy` — Prometheus healthy.
4. `docker compose exec tempo wget -qO- http://localhost:3200/ready` — `ready`.
5. `curl http://localhost:3000/api/health` — Grafana `database: ok`.
6. Prometheus → Status → Targets: `otel-collector` is `UP`.
7. Grafana → Connections → Data sources → Prometheus / Tempo → **Save & test**
   both succeed.
8. Start `IranDirect.Service` with the overrides from §9. Within ~30s query
   `irandirect_runtime_cycles_started` in Prometheus and search Tempo for
   `service.name = IranDirect.Service`.
8b. Grafana → Dashboards → `IranDirect` folder shows the five Phase 33.3
    dashboards; open **Service Overview** and confirm the cycle-rate / latency
    panels populate within ~1 minute.
8c. `curl -s 'http://localhost:9090/api/v1/rules'` shows group
    `irandirect_recording` with 20 rules and no `lastError` entries.
9. Set `Observability__Enabled=false`, restart the Service, confirm normal
   operation and no new telemetry.
10. `docker compose stop`, restart the Service with telemetry enabled, confirm
    the Service still functions with the collector unavailable.

## 13. Troubleshooting

| Symptom | Check |
|---------|-------|
| No metrics in Prometheus | Is `Observability:Enabled` **and** `Observability:Otlp:Enabled` true? Is the endpoint `http://localhost:4317`? Is the `otel-collector` target UP? |
| No traces in Tempo | Is `TracingEnabled` true and `SamplingRatio > 0`? Check collector logs for OTLP export errors to `tempo:4317`. |
| Grafana will not start | `GF_SECURITY_ADMIN_USER`/`GF_SECURITY_ADMIN_PASSWORD` must be set in `.env`. |
| Need collector-side visibility | Set `OTEL_DEBUG_VERBOSITY=normal` in `.env` and recreate the collector. Development only. |
