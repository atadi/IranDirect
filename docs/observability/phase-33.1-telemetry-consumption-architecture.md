# Phase 33.1 — Telemetry Consumption Architecture

Analysis, deployment design, and documentation only. No infrastructure, no
deployment files, no source changes, no new packages. The design consumes the
telemetry emitted by Phases 32.2–32.9 unchanged.

## 1. Scope and non-goals

This phase designs the complete telemetry-consumption platform:
OpenTelemetry Collector, trace storage, metric storage, dashboarding,
alerting, retention, privacy, authentication, network exposure, backup/recovery,
operational ownership, failure isolation, and a phased implementation roadmap.

**Non-goals (explicit):**
- No Docker / Compose / Kubernetes manifests created (that is Phase 33.2).
- No collector deployed.
- No application source or OpenTelemetry registration modified.
- No dashboards, alerts, or provisioning YAML/JSON created (Phases 33.3–33.4).
- No vendor-specific packages added to the application.
- No re-instrumentation; the application telemetry contract is frozen.

## 2. Current telemetry inventory (definitive)

All names are verbatim from `IranDirect.Core/Observability/Telemetry/*` and
`IranDirect.Service/Observability/*`.

### Identity
- **ActivitySource name:** `IranDirect.Core`
- **Meter name:** `IranDirect.Core`
- **Source/meter version:** Core assembly informational version (`IranDirectTelemetry.Version`).
- **Resource attributes:** `service.name` (`IranDirect.Service`), `service.version` (Core assembly version), `deployment.environment.name` (config or host env name). `service.instance.id` is intentionally **not** emitted.

### Root spans (5)
| Span | Source constant |
|------|-----------------|
| `IranDirect.RuntimeCycle` | runtime reconciliation top-level cycle |
| `IranDirect.PrefixUpdateCheck` | official prefix update check |
| `IranDirect.CustomRouteRefresh` | custom-route DNS refresh |
| `IranDirect.IpcRequest` | named-pipe IPC request/response (client root) |
| `IranDirect.SupportBundleExport` | support snapshot/bundle export (shared root) |
| `Ipc.Dispatch` | independent IPC server dispatch root (Phase 32.8) |

### Child spans
- Runtime: `Runtime.Observe`, `Runtime.BuildDecision`, `Runtime.BuildPreview`, `Runtime.PlanChanges`, `Runtime.Execute`, `Runtime.PersistInventory`
- Routes: `Routes.Enumerate`, `Routes.Create`, `Routes.Delete`
- Prefix: `Prefix.HttpHead`, `Prefix.HttpGet`, `Prefix.Compare`, `Prefix.PersistMetadata`
- DNS: `Dns.CacheRead`, `Dns.Resolve`, `Dns.CacheWrite`
- IPC: `Ipc.Connect`, `Ipc.Send`, `Ipc.Receive`
- Support: `Support.CaptureSnapshot`, `Support.Serialize`, `Support.WriteJson`, `Support.CreateZip`

### Counters (15) — `irandirect.*`
`runtime.cycles.started`, `runtime.cycles.completed`, `runtime.cycles.failed`,
`runtime.cycles.cancelled`, `runtime.repairs.with_changes`,
`runtime.repairs.no_changes`, `routes.operations.requested`,
`routes.operations.succeeded`, `routes.operations.failed`, `prefix.checks`,
`dns.lookups`, `ipc.requests`, `support.bundles.exported`,
`support.bundles.failed`.

### Histograms (11) — `irandirect.*`
`runtime.cycle.duration`, `runtime.observe.duration`,
`runtime.planning.duration`, `runtime.execution.duration`,
`routes.system_call.duration`, `prefix.check.duration`, `dns.lookup.duration`,
`ipc.request.duration`, `support.bundle.duration`,
`runtime.operations.per_cycle`, `runtime.changed_routes`.

### Gauges (6) — `irandirect.*`
`service.enabled`, `runtime.worker.active`, `prefix.known_count`,
`routes.inventory_count`, `dns.cache_record_count`, `prefix.consecutive_failures`.

### Approved tag names (bounded, lower_snake_case)
`operation`, `outcome`, `trigger`, `route_kind`, `change_kind`, `source`,
`cache_state`, `ipc_command`, `diagnostic_severity`, `service_state`,
`failure_category`.

### Bounded tag values (closed enumerations — `IranDirectTagValues`)
- `outcome`: success, failure, cancelled, timeout, no_change, unknown
- `operation`: runtime_cycle, plan_changes, execute, enumerate_routes, create_routes, delete_routes, prefix_update_check, prefix_http_head, prefix_http_get, prefix_compare, prefix_persist_metadata, custom_route_refresh, dns_cache_read, dns_resolve, dns_cache_write, ipc_request, ipc_connect, ipc_send, ipc_receive, ipc_dispatch, support_snapshot_export, support_bundle_export, support_capture_snapshot, support_serialize, support_write_json, support_create_zip
- `trigger`: scheduled, forced, cli, tray, startup, repair, unknown
- `route_kind`: prefix, endpoint, unknown
- `change_kind`: create, delete, unknown
- `source`: official, custom, cache, unknown
- `cache_state`: fresh, stale, miss, failed, unknown
- `diagnostic_severity`: pass, warning, failure, unknown
- `service_state`: enabled, disabled, unknown
- `failure_category`: io, timeout, cancellation, http, dns, routing, serialization, invalid_response, unknown

### Prohibited tag names (never emitted — `IranDirectTagNames.Prohibited`)
`destination_prefix`, `gateway`, `next_hop`, `interface_index`,
`interface_name`, `domain`, `dns_domain`, `output_path`, `file_path`,
`pipe_payload`, `machine_name`, `user_name`, `exception_message`, `url`,
`endpoint`, `route_identity`, `diagnostic_id`, `execution_step_identity`.

### Defaults and export
- **Default state:** telemetry export **disabled** (`Observability:Enabled=false`); no provider/exporter created.
- **Sampling:** parent-based ratio (`ParentBased(TraceIdRatioBased(ratio))`); default config `SamplingRatio=1.0`, bounded 0.0–1.0.
- **OTLP:** gRPC (`4317`) or HTTP/protobuf (`4318`); endpoint from config; headers from a named environment variable (never committed).
- **Console exporter:** Development environment only, `Console.Enabled=true`.
- **Privacy:** no URLs, domains, IPs, prefixes, paths, payloads, machine/user names, or exception messages on any span/metric/resource attribute.

### Current gaps
- No trace/metric backend exists; export terminates nowhere when disabled.
- No dashboards, recording rules, or alerts.
- No collector deployment, TLS, or auth story yet.
- No logs pipeline (application logs are via `Microsoft.Extensions.Logging`, not OTel logs — out of scope for 33.1).
- No cross-installation aggregation design (each host is independent unless a central Collector is chosen later).

## 3. Deployment assumptions

Derived from repository evidence and stated explicitly where evidence is absent.

**Confirmed by repository:**
- `IranDirect.Service` runs as a **Windows Service** (`UseWindowsService`, `Microsoft.Extensions.Hosting.WindowsServices`). It is the sole route-mutation authority.
- No Docker / Compose / Kubernetes artifacts exist in the repository.
- OpenTelemetry packages are confined to `IranDirect.Service` (+ `IranDirect.Service.Tests`); Core is BCL-only.

**Assumptions (documented, not confirmed):**
- A1. The host is a long-lived Windows machine (server or workstation) with local disk and outbound network access that may be restricted/firewalled.
- A2. Docker Desktop / container runtime is **not** assumed present on production hosts; Phase 33.2 may instead use a dedicated Linux observability host or Windows-native services. Primary recommendation below favors a **separate Linux observability host** running the stack via Docker Compose, reachable from the Windows Service over the LAN.
- A3. Installations may operate **offline**; the consumption platform must be optional and never a runtime dependency (already guaranteed by Phase 32.9 failure isolation).
- A4. Secrets (OTLP headers, Grafana admin, notification webhooks) are delivered via environment variables or mounted secret files, not committed.
- A5. No existing reverse proxy / TLS termination is assumed for the observability stack; TLS is terminated at the Collector/Grafana or behind an org proxy if present.
- A6. Multiple IranDirect installations are possible; each may ship its own local Collector, or several may forward to a central Collector (multi-installation option evaluated in §4/§5).

## 4. Candidate stack evaluation

Comparison based on stable architectural knowledge of each project's license,
storage model, and Windows/Docker suitability. No web research used.

### Option A — Collector + Prometheus + Tempo + Grafana
- **License:** Apache-2.0 (all components).
- **Self-host:** Yes. Prometheus/Tempo/Grafana are single static binaries or containers; Collector Contrib is a single binary.
- **Windows:** Prometheus/Tempo run natively on Windows (binaries) and under WSL; Grafana has a Windows build; Collector Contrib has a Windows binary. Docker also viable.
- **Docker:** First-class images for all four.
- **Footprint:** Prometheus ~300–600 MB RAM steady + WAL; Tempo ~500 MB–1 GB; Grafana ~100–300 MB; Collector ~50–150 MB. Total conservative ~1.5–2.5 GB RAM on one host.
- **Storage:** Prometheus TSDB (local block + WAL, filesystem); Tempo block storage (filesystem or object store).
- **Traces:** Tempo ingests OTLP natively; good search by service/SPAN/trace ID.
- **Metrics:** Prometheus pulls/scrapes OTLP-exposed metrics (Collector `prometheus` exporter) or receives remote-write.
- **Dashboards:** Grafana first-class.
- **Alerting:** Prometheus Alertmanager native.
- **Retention:** Prometheus `--storage.tsdb.retention` (time-based); Tempo `compaction.block_retention`.
- **Auth:** Grafana built-in users/roles + OAuth; Collector OTLP auth via extensions; Prometheus/Tempo usually LAN-internal only.
- **Backup:** Prometheus TSDB snapshot; Tempo block copy; Grafana provisioning in git.
- **Upgrade:** Binary/container swap; low risk, well documented.
- **Maturity:** Very high; de-facto standard CNCF stack.
- **Single-host:** Excellent.
- **Multi-install:** Good (central Collector or central Prometheus/Tempo).
- **Offline:** Fully self-hosted, no SaaS dependency.
- **Failure modes:** Prometheus disk full → ingestion stops (guarded by retention + alerts); Tempo backend down → Collector buffers bounded then drops; Grafana down → no impact on ingestion.
- **Maintenance:** Low–moderate; the most documented path.

### Option B — Collector + Grafana Alloy + Prometheus + Tempo + Grafana
- Alloy consolidates Collector + Prometheus-agent + Tempo pipeline into one OTel-native agent.
- **License:** Apache-2.0.
- **Pros:** fewer moving parts (one agent); native OTel pipeline config.
- **Cons:** Alloy still needs Prometheus + Tempo + Grafana as stores; adds an abstraction layer; smaller ops community than vanilla Collector+Prometheus; Windows support improving but less battle-tested than Option A.
- **Footprint:** similar to A but consolidated.
- **Assessment:** Reasonable, but adds a component the team must learn; Option A is simpler to reason about and matches the existing application-side OTel model 1:1.

### Option C — Collector + VictoriaMetrics + Tempo + Grafana
- **License:** VictoriaMetrics is Apache-2.0 (Community) / proprietary (Enterprise cluster).
- **Pros:** VictoriaMetrics has much lower RAM/disk than Prometheus at scale, higher cardinality headroom, remote-write native (matches Collector `prometheusremotewrite` exporter), single binary.
- **Cons:** different PromQL edge cases; Enterprise features (clustering, auth) are commercial; smaller team may prefer Prometheus familiarity.
- **Footprint:** ~2–4× lower than Prometheus for equivalent data.
- **Assessment:** Strong for scale; for current bounded telemetry Prometheus is sufficient, so VictoriaMetrics is a later upgrade path, not the initial default.

### Option D — Collector + ClickHouse-based platform
- e.g. ClickHouse for traces/metrics (or Uptrace).
- **License:** ClickHouse Apache-2.0; Uptrace has open + commercial tiers.
- **Pros:** unified store, very high query power.
- **Cons:** significantly higher operational complexity (columnar DB ops, schema, backups); overkill for current low-cardinality telemetry; longer time-to-value.
- **Assessment:** Rejected for initial deployment; revisit only if telemetry volume or query needs explode.

### Option E — Collector only, forward to existing external platform
- Collector exports OTLP to a customer/central Grafana Cloud / other backend.
- **Pros:** no self-hosted storage to operate.
- **Cons:** depends on external SaaS/contract; may conflict with offline/air-gapped deployments (assumption A3); ongoing cost; less control over retention/privacy.
- **Assessment:** Valid fallback for hosted environments, but the default recommendation must support offline/self-hosted (A3).

## 5. Selected reference architecture

### Primary: Option A — OpenTelemetry Collector Contrib + Prometheus + Tempo + Grafana

**Why sufficient for current telemetry:**
- Total active series are low (bounded enum tags; see §7 cardinality). Prometheus handles this with trivial resource cost.
- All spans/metrics are OTLP-native; the Collector translates to Prometheus metrics and Tempo traces with zero application change.
- CNCF-standard, maximally documented, lowest operational surprise.

**Why logs are not included:**
- Application logging uses `Microsoft.Extensions.Logging`, not OTel logs. Loki/OTel logs pipeline is explicitly deferred. Adding logs now would expand scope, storage, and privacy surface with no current contract. Non-goal for 33.1.

**Why Loki is not included:**
- No log telemetry is emitted by the application; introducing Loki would store host/app logs that are out of the frozen telemetry contract and raise privacy/cardinality concerns. Deferred.

**Why direct-to-backend application export is rejected:**
- Phase 32.9 already rejects direct app→backend coupling. The Collector is the only application-facing OTLP endpoint; it decouples the app from backend auth/endpoints, enables tail sampling/transform later, and centralizes TLS/auth. Direct export would also require per-backend creds in the app config (privacy risk) and break the "Collector is the boundary" principle.

**Why the Collector is the only app-facing OTLP endpoint:**
- Single tunnel; the Windows Service exports only to `http(s)://collector:4317` (or 4318). Backends (Prometheus/Tempo/Grafana) are never directly reachable by the app. This contains auth, retries, and failure domains.

**Evolution path:**
- Start Prometheus+Tempo on one host. Later: remote-write to VictoriaMetrics (C), object storage for Tempo, central Collector for multiple installs (E), or add Loki once log telemetry exists.

### Fallback: Option E (Collector → existing Grafana Cloud / central backend)
- Chosen if the deployment is hosted, has a Grafana Cloud (or equivalent) contract, and offline operation (A3) is not required. Keeps the Collector boundary; delegates storage/UI. Implementation in 33.2 becomes a Collector-only compose + app config, with no self-hosted Prometheus/Tempo/Grafana.

## 6. Collector design (application-facing boundary)

Deployed as `otelcol-contrib` on the observability host (or same Windows host in dev). It is the sole OTLP receiver.

### Receivers
- `otlp` over gRPC `0.0.0.0:4317` (primary).
- `otlp` over HTTP `0.0.0.0:4318` (optional, useful for `http/protobuf` app config and health probes).

### Processors
- `memory_limiter` — first in pipeline; `check_interval=1s`, `limit_mib=400`, `spike_limit_mib=100` (tune to host). Protects the host from OOM; forces drop under pressure.
- `batch` — `timeout=5s`, `send_batch_size=8192`, `send_batch_max_size=10000`. Reduces export calls.
- `resource` — ensures `service.name`/`service.version`/`deployment.environment.name` are present (defensive; app already sets them). No sensitive attributes added.
- `attributes` — **not used** by default (app tags are already bounded/approved). Added later only if a backend needs renaming; must never copy prohibited fields.
- `tail_sampling` — **not in initial deployment**. Deferred (§9).
- `transform` — **not used** initially.

### Exporters
- `prometheus` — exposes metrics at `0.0.0.0:8889/metrics` for Prometheus to scrape. (Alternative: `prometheusremotewrite` to VictoriaMetrics later.)
- `otlp` → Tempo at `tempo:4317` (gRPC).
- `debug` — **development only**, `verbosity=normal`; never enabled in production (no telemetry payloads logged).
- No production logging exporter.

### Extensions
- `health_check` — `0.0.0.0:13133`; used by container health checks and startup-order gates.
- `pprof` — **omitted** unless locally bound for profiling; if used, bind `127.0.0.1:1777` only.
- `zpages` — **omitted** unless locally bound for debugging; bind `127.0.0.1:55679` only.

### Policies
- **Bind addresses:** receivers on the LAN interface or loopback per environment (dev loopback, prod LAN with firewall). Never public internet.
- **TLS:** OTLP receiver TLS terminated at Collector (or upstream proxy) in production; self-signed/CA per site. App uses `https://collector:4317`.
- **Authentication:** OTLP auth via `headers` (the `Otlp.HeadersEnvironmentVariable` value) OR mTLS; Collector validates and drops unauthenticated pushes.
- **Queue/retry:** OTLP exporter `sending_queue` (`queue_size=5000`), `retry_on_failure` (`enabled=true`, `initial_interval=2s`, `max_interval=30s`, `max_elapsed_time=300s`). Bounded — no infinite retry.
- **Shutdown:** `max_batch_size` + graceful drain; Collector honors SIGTERM with bounded flush (SDK default; §11 of 32.9).

### Sensitive values
- No sensitive tag values ever reach the Collector (app contract forbids them). Collector config stores no app secrets; OTLP header value comes from the app's env var, not collector config.

## 7. Metrics storage design (Prometheus)

### Model
- Prometheus **scrapes** the Collector's `prometheus` exporter at `collector:8889/metrics`. (Remote-write to VictoriaMetrics is a later option, not initial.)
- **Scrape interval:** 15s (default). **Evaluation interval:** 15s.
- **Retention:** 30 days local (configurable), time-based (`--storage.tsdb.retention.time=30d`).
- **WAL:** enabled; `wal_segment_size` default; protects against crash. Compaction: default TSDB block compaction (2h blocks).
- **Disk sizing assumption:** see §15.

### Label-cardinality guardrails
- All labels are the bounded enum tags from §2. Worst-case distinct label-combos per metric are small (see below). **No high-cardinality labels** (no IDs, timestamps, paths) — enforced by the app contract and architecture tests.
- Guardrail rule: reject any future metric with a label whose cardinality can exceed ~100; alert if a series-count spike is observed (Prometheus `scrape_samples_scraped` anomaly).

### Metric naming expectations
- Exactly the `irandirect.*` names from §2. `job="irandirect-service"`, `instance`, plus `deployment.environment.name` from resource attrs.

### Recording-rule strategy
- Precompute expensive rates/percentiles (§11) to keep dashboards/alerts cheap.

### Alert-rule ownership
- Alert rules live in version-controlled Prometheus/`rules/` (Phase 33.4), not in the app.

### Cardinality estimate (conservative upper bounds)
Assumptions: bound each enum at its max distinct value; multiply only across the labels actually attached to each instrument.

- **runtime.cycles.{started,completed,failed,cancelled}** — labeled `outcome` (≤6) and `trigger` (≤7). Worst ≈ 6×7 = 42 series per counter; 4 counters ≈ **168**.
- **runtime.repairs.{with_changes,no_changes}** — `outcome` (≤6) ≈ 12 series each; 2 ≈ **24**.
- **routes.operations.{requested,succeeded,failed}** — `operation` (enumerate/create/delete ≈3) + `outcome` (≤6) ≈ 18 each; 3 ≈ **54**.
- **prefix.checks** — `outcome` (≤6) + `trigger` (≤7) ≈ 42; **1 counter ≈ 42**.
- **dns.lookups** — `outcome`/`source`/`cache_state` combos ≤ 6×4×5 = 120; **≈ 120**.
- **ipc.requests** — `ipc_command` (≤5) + `outcome` (≤6) ≈ 30; **≈ 30**.
- **support.bundles.{exported,failed}** — `outcome` (≤6) ≈ 12; 2 ≈ **24**.
- **Histograms (11)** — each partitioned by the same bounded labels; assume ≤100 series each worst case → **≈ 1,100** (generous; realistic far lower).
- **Gauges (6)** — single or low-cardinality (e.g. `prefix.known_count` unlabeled) → **≤ 20**.

**Total active series upper bound ≈ 1,600** (realistic steady-state well under 1,000). Prometheus handles tens of thousands comfortably; this is a very light load.

## 8. Trace storage design (Tempo)

### Model
- Collector `otlp` exporter → Tempo `otlp` receiver (`tempo:4317`).
- **Local filesystem** blocks initially (`local` backend); object storage (S3/GCS/Azure Blob) is a later upgrade for multi-host/HA.
- **Retention:** 7–14 days production, 1–3 days development (`compaction.block_retention`).
- **Block duration:** 5m; **compaction:** Tempo default (compactor compacts blocks).
- **Sampling:** see §9. Head-based ratio in the app; Tempo stores what the app sends.
- **Expected trace volume:** runtime cycles are periodic (e.g. every few minutes) + on-demand (forced/cli/tray/repair). Prefix checks and DNS refreshes are periodic. IPC requests occur on command/control. Support exports are rare. Worst-case busy host: low hundreds of traces/minute. Spans per workflow: RuntimeCycle tree ≈ 8–15 spans (observe→decision→plan→execute→persist + route/DNS children), IPC ≈ 4, support ≈ 5.
- **Search/index:** Tempo search by `service.name`, `deployment.environment.name`, span name, status; tag-based search limited to indexed attributes (the bounded app tags are ideal — low cardinality, indexed-friendly).
- **Trace-to-metrics correlation:** Grafana `trace to metrics` uses `service.name` + span name; `metrics to trace` via exemplars where histograms expose them.
- **Trace-ID in ops:** error alerts link to a representative trace ID (Tempo search by `failure_category` + `outcome=failure`); no payload/path/identity stored.

### Trace volume estimate (conservative)
- Runtime cycles: ≤ 1/min busy → ~1,440/day.
- Planning/execution: subset of cycles.
- Route native calls: per cycle ≤ ~30 ops → ~43k/day.
- Prefix checks: ≤ 1/min → ~1,440/day.
- DNS refreshes: ≤ 1/min → ~1,440/day.
- IPC requests: bursty, ≤ 10/min → ~14k/day.
- Support exports: rare, ≤ 10/day.
- Spans/day upper bound ≈ **~60k** (mostly route native calls). At ~1–2 KB/span → ~60–120 MB/day raw; Tempo compression + 7–14d retention → **< 2 GB** production trace store. Development (1–3d) negligible.

### Privacy in traces
- No IPC payloads, file paths, domains, IPs, route identities, machine/user names, or exception messages (prohibited tags enforced in app + architecture tests). Tempo stores only approved spans/attributes.

## 9. Sampling strategy

Metrics are **never sampled** (Prometheus scrapes all series). Traces use head-based parent ratio sampling, already implemented in the Service (`ParentBased(TraceIdRatioBased(ratio))`).

Recommendations:
- **Development:** `SamplingRatio=1.0` (100%) — full fidelity, local Collector.
- **Test:** telemetry **disabled by default**; integration tests use in-memory exporters (Phase 32.9). When a test intentionally enables OTLP, use a local Collector; no shared external backend.
- **Production:** conservative default **`SamplingRatio=0.25` (25%)** for root spans, given low absolute volume and the need to retain error visibility. Rationale: at <60k spans/day even 100% is cheap, but 25% bounds future growth and keeps storage predictable; error/timeout traces are still well represented because they are a meaningful fraction of volume. (If volume proves trivially small in 33.6, raise to 1.0.)

**Tail sampling (deferred):** not in initial deployment. If adopted later:
- Collector adds `tail_sampling` with policies: always-keep `outcome=failure`/`timeout`, keep `duration > p99`, keep errors; drop the rest to a low baseline.
- Cost: tail sampler buffers traces in memory until decision → RAM increase (~hundreds of MB at modest volume) and added latency; requires the Collector to see whole traces (no early export). Justified only if head sampling loses too many errors or storage cost grows. Initial deployment does not require it.

## 10. Dashboard architecture (Grafana)

Five initial dashboards. Every panel maps to a committed metric/histogram; no panel uses an unavailable metric.

### Dashboard 1 — Service Overview
| Panel | Source metric | Agg | Labels | Unit | Range | Type | Drill-down |
|-------|--------------|-----|-------|-----|------|------|-----------|
| Telemetry enabled | `service.enabled` | instant | — | bool | 6h | stat | — |
| Worker active | `runtime.worker.active` | instant | — | bool | 6h | stat | — |
| Cycles by outcome | `runtime.cycles.started` | sum by `outcome` | outcome | count | 24h | timeseries/bar | Dashboard 2 |
| Cycle duration p50/p95/p99 | `runtime.cycle.duration` | histogram_quantile | — | s | 24h | timeseries | Dashboard 2 |
| Repair vs no-change | `runtime.repairs.with_changes` / `.no_changes` | sum | — | count | 7d | bar | Dashboard 2 |
| Prefix count | `prefix.known_count` | instant | — | count | 6h | stat | Dashboard 3 |
| Managed routes | `routes.inventory_count` | instant | — | count | 6h | stat | Dashboard 2 |

### Dashboard 2 — Runtime Reconciliation
| Panel | Source | Agg | Labels | Unit | Range | Type |
|-------|-------|-----|-------|-----|------|------|
| Planning duration | `runtime.planning.duration` | p50/p95/p99 | — | s | 24h | timeseries |
| Execution duration | `runtime.execution.duration` | p50/p95/p99 | — | s | 24h | timeseries |
| Changed routes | `runtime.changed_routes` | sum | — | count | 24h | timeseries |
| Operations per cycle | `runtime.operations.per_cycle` | avg | — | count | 24h | timeseries |
| Execution outcomes | `runtime.cycles.completed/failed/cancelled` | sum by `outcome` | outcome | count | 24h | bar |
| Route-system-call latency | `routes.system_call.duration` | p95 | — | s | 24h | timeseries |
| Route-operation failures | `routes.operations.failed` vs `.requested` | rate | operation | ratio | 24h | timeseries |
| Trace links | from any error panel | — | service.name + span | — | — | trace drill-down |

### Dashboard 3 — Prefix and DNS
| Panel | Source | Agg | Labels | Unit | Range | Type |
|-------|-------|-----|-------|-----|------|------|
| Prefix checks by outcome | `prefix.checks` | sum by `outcome` | outcome | count | 24h | bar |
| Prefix-check latency | `prefix.check.duration` | p95 | — | s | 24h | timeseries |
| DNS lookup rate | `dns.lookups` | rate | source | ops/s | 24h | timeseries |
| DNS failures | `dns.lookups{outcome="failure"}` | rate | source | ops/s | 24h | timeseries |
| DNS latency | `dns.lookup.duration` | p95 | — | s | 24h | timeseries |
| Cache-state distribution | from traces `cache_state` (Dns.* spans) where metrics don't expose it | count by `cache_state` | cache_state | count | 24h | bar |

### Dashboard 4 — IPC and Support
| Panel | Source | Agg | Labels | Unit | Range | Type |
|-------|-------|-----|-------|-----|------|------|
| IPC request rate | `ipc.requests` | rate | ipc_command | ops/s | 24h | timeseries |
| IPC outcomes | `ipc.requests` | sum by `outcome` | outcome | count | 24h | bar |
| IPC latency | `ipc.request.duration` | p95 | ipc_command | s | 24h | timeseries |
| Timeout/failure categories | `ipc.requests{outcome=~"timeout|failure"}` | sum by `failure_category` | failure_category | count | 24h | bar |
| Support exports succeeded/failed | `support.bundles.exported` / `.failed` | sum | outcome | count | 7d | stat |
| Support-export latency | `support.bundle.duration` | p95 | — | s | 7d | timeseries |

### Dashboard 5 — Reliability and Errors
| Panel | Source | Agg | Labels | Unit | Range | Type |
|-------|-------|-----|-------|-----|------|------|
| Failure-category trend | `runtime.cycles.failed` + `routes.operations.failed` + `ipc.requests` | sum by `failure_category` | failure_category | count | 7d | timeseries |
| Cancelled vs failed | `runtime.cycles.cancelled` vs `.failed` | sum | — | count | 7d | bar |
| Route native failures | `routes.operations.failed` | rate | operation | ratio | 24h | timeseries |
| Timeout trend | `ipc.requests{outcome="timeout"}` + `runtime.cycles.{failed,timeout}` | rate | — | ops/s | 7d | timeseries |
| Error trace links | TopN by `failure_category` | — | trace search | — | — | Tempo link |

## 11. Recording rules

Naming: `irandirect:recording:<subject>_<metric>` (group `irandirect_recording`).
Use exact committed metric names.

```
groups:
  - name: irandirect_recording
    interval: 30s
    rules:
      - record: irandirect:recording:cycle_success_rate
        expr: sum(rate(irandirect_runtime_cycles_completed[5m]))
              / sum(rate(irandirect_runtime_cycles_started[5m]))
      - record: irandirect:recording:cycle_failure_rate
        expr: sum(rate(irandirect_runtime_cycles_failed[5m]))
              / sum(rate(irandirect_runtime_cycles_started[5m]))
      - record: irandirect:recording:cycle_duration_p95
        expr: histogram_quantile(0.95,
              sum by (le) (rate(irandirect_runtime_cycle_duration_bucket[5m])))
      - record: irandirect:recording:cycle_duration_p99
        expr: histogram_quantile(0.99,
              sum by (le) (rate(irandirect_runtime_cycle_duration_bucket[5m])))
      - record: irandirect:recording:route_op_success_rate
        expr: sum(rate(irandirect_routes_operations_succeeded[5m]))
              / sum(rate(irandirect_routes_operations_requested[5m]))
      - record: irandirect:recording:dns_lookup_failure_ratio
        expr: sum(rate(irandirect_dns_lookups{outcome="failure"}[5m]))
              / sum(rate(irandirect_dns_lookups[5m]))
      - record: irandirect:recording:ipc_timeout_ratio
        expr: sum(rate(irandirect_ipc_requests{outcome="timeout"}[5m]))
              / sum(rate(irandirect_ipc_requests[5m]))
      - record: irandirect:recording:support_export_failure_ratio
        expr: sum(rate(irandirect_support_bundles_failed[5m]))
              / sum(rate(irandirect_support_bundles_exported[5m]))
```

(No invented metrics; all sources are committed names. `le` label is auto-added by Prometheus histograms.)

## 12. Alert catalog

Alerts in version-controlled Prometheus rules (Phase 33.4). Concepts use the recording rules above where possible. `for` prevents single-event noise.

| Alert | Query concept | Threshold | Window | `for` | Severity | Route |
|-------|--------------|-----------|--------|------|----------|-------|
| ServiceTelemetryAbsent | `absent(irandirect_runtime_cycles_started)` | no samples | 15m | 10m | critical | on-call |
| RuntimeCycleFailures | `irandirect:recording:cycle_failure_rate` | > 0.25 | 15m | 10m | warning→critical | on-call |
| RouteOpFailureRatio | `irandirect:recording:route_op_success_rate` | < 0.90 | 15m | 10m | warning | on-call |
| RuntimeCycleP95Latency | `irandirect:recording:cycle_duration_p95` | > 300s | 15m | 15m | warning | dashboard |
| PrefixChecksFailing | `increase(irandirect_prefix_checks{outcome="failure"}[1h])` | ≥ 10 consecutive | 1h | 30m | warning | on-call |
| DnsLookupFailures | `irandirect:recording:dns_lookup_failure_ratio` | > 0.10 | 15m | 10m | warning | on-call |
| IpcTimeoutRatio | `irandirect:recording:ipc_timeout_ratio` | > 0.10 | 15m | 10m | warning | on-call |
| SupportExportFailures | `increase(irandirect_support_bundles_failed[1h])` | ≥ 3 | 1h | 15m | warning | on-call |
| CollectorUnavailable | `up{job="otel-collector"} == 0` | 1 | 5m | 2m | critical | on-call |
| PrometheusDiskNearFull | `predict_linear(prometheus_tsdb_storage_blocks_bytes[6h], 4h) > 0.9 * capacity` | > 90% | 6h | — | warning | infra |
| TempoIngestionFailing | Tempo `metrics` `tempo_distributor_write_errors_total` rising | > 0 | 15m | 5m | warning | infra |

**Severity levels:** `info` (dashboard only), `warning` (notify during business hours, page if recurring), `critical` (page immediately, 24×7).
**Notification routes:** see §13.
**False-positive risks:** transient network blips on `RouteOpFailureRatio`/`DnsLookupFailures` — mitigated by `for` + rate over 15m; `RuntimeCycleFailures` during planned maintenance — suppressed via maintenance silence.
**Inhibition:** `CollectorUnavailable` inhibits downstream per-metric alerts that depend on ingestion (avoids storm when the Collector itself is down, not the app).

## 13. Notification routing

No credentials in rules; endpoints injected via env/secret file at deploy time.

- **Email** (SMTP relay) — warning/critical, business hours.
- **Slack/Teams webhook** — warning/critical, after-hours page channel.
- **PagerDuty / equivalent** — critical only, 24×7.
- **Local-only dashboard notification** — info/warning, no external send (default for air-gapped installs).

**Routing logic:**
- `critical` → PagerDuty + Slack/Teams + Email.
- `warning` → Slack/Teams (business hours) or Email (off-hours, no page).
- `info` → Grafana annotation only.
- **Deduplication/grouping:** by `alertname` + `instance`/`job`; `group_wait=30s`, `group_interval=5m`, `repeat_interval=4h`.
- **Inhibition:** Collector-down inhibits dependent alerts.
- **Secrets:** webhook URLs / API keys from env vars or mounted secret files (e.g. `/run/secrets/alertmanager.yaml`), never committed.

## 14. Privacy and security architecture

**Trust boundaries:** App host (Windows Service) → Collector (LAN) → Prometheus/Tempo/Grafana (observability host, internal network only). Nothing is publicly exposed.

- **OTLP auth:** Collector requires OTLP header (from app env var) or mTLS; unauthenticated pushes dropped.
- **TLS:** OTLP receiver + Grafana behind TLS in production; self-signed/CA per site. Prometheus/Tempo scraped internally over the LAN (optionally mTLS).
- **Firewall:** only `4317/4318` (Collector) and `3000` (Grafana, admin) open on the observability host; Prometheus `9090` and Tempo `3200` bound to internal/LAN only, not public.
- **Grafana auth:** local users + admin role; viewer role for read-only ops; admin provisioned from secret, not default `admin/admin`.
- **Least privilege:** Collector/Prometheus/Tempo/Grafana run as non-root service accounts; filesystem perms limit TSDB/block dirs.
- **Data at rest:** Prometheus TSDB + Tempo blocks on encrypted volume (BitLocker/luks) where required; rotation of any stored secrets.
- **Retention deletion:** time-based auto-deletion (§15); no manual purge of sensitive data needed because none is stored.
- **Support access:** operational access via Grafana viewer + runbooks; no direct production host login without change control.
- **Audit:** Grafana admin actions logged; Collector config version-controlled.

**Explicitly prohibited in storage (enforced by app contract + architecture tests):**
route identities, destination prefixes, gateways, interface identifiers, DNS domains, IP addresses, IPC payloads, support bundle contents, file paths, machine/user names, exception messages.

**Allowed resource attributes:** `service.name`, `service.version`, `deployment.environment.name` only.

## 15. Retention and sizing

### Retention
- **Metrics:** 30 days local (Prometheus). Adjustable; recording rules keep long-term rates if raw trimmed.
- **Traces:** 7–14 days production; 1–3 days development (Tempo `block_retention`).

### Sizing formulas (estimates, not measured facts)
Assumptions (conservative):
- Runtime cycles: 1/min → 1,440/day.
- Route native ops/cycle: 30 → 43,200/day.
- DNS/prefix: 1/min each.
- IPC: 10/min → 14,400/day.
- Bytes/sample (Prometheus): ~2 KB/series-sample at 15s scrape → per series ≈ 2 KB × (86400/15) ≈ 115 MB/day raw per series before compaction; with ~1,600 series and ~2× compaction overhead + safety margin → **≈ 350–500 MB/day metrics**; 30d → **~10–15 GB** metrics store (comfortably small).
- Traces: ~60k spans/day × ~1.5 KB → ~90 MB/day; 7–14d → **< 2 GB** trace store.
- Replicas/RF: 1 (single host) initially; RF=1 assumed. Add RF later for HA.
- Safety margin: ×1.5 capacity headroom.

**Total observability host disk (initial, single host):** metrics ~15 GB + traces ~2 GB + Grafana/Prometheus/Tempo binaries + WAL ~5 GB ≈ **25–30 GB**, plus 1.5× margin → **~45 GB** usable. RAM ~2 GB (§4A). These are planning estimates; 33.6 validates against real volume.

## 16. Backup and disaster recovery

**Version-controlled (must back up in git, not telemetry):**
- Collector config (`otel-collector.yaml`).
- Prometheus `prometheus.yml` + `rules/`.
- Tempo config.
- Grafana provisioning (dashboards, datasources, alert contact points) — as code.
- Alertmanager config (secrets via env/secret file refs, not values).

**Disposable (do not back up; reproducible):**
- Prometheus TSDB raw blocks — can be rebuilt from the app over 30d; backing up is optional.
- Tempo blocks — disposable; traces regenerate as the app runs.
- Collector/Prometheus/Tempo runtime state.

**Should back up (if retention matters beyond live window):**
- Prometheus TSDB snapshot (optional, for historical audit) — low priority.
- Grafana SQLite/Postgres (dashboards/users) — covered by provisioning-as-code; back up if using DB mode.

**RTO / RPO:**
- RTO for observability: < 1 hour (redeploy stack from git).
- RPO: telemetry data loss acceptable (observability is diagnostic, not transactional); no business data at risk.

**Preference:** reproducible configuration over backing up all telemetry data. A fresh deploy from git restores full capability; only recent metrics/traces are lost, which is acceptable.

## 17. Deployment topology

### Component diagram (logical)
```
[Windows Service: IranDirect.Service]
   OTLP (TLS+auth) :4317/:4318
        |
        v
[Observability Host / Docker Compose]
   otel-collector  (recv 4317/4318, exp prometheus:8889, otlp->tempo:4317, health :13133)
        |                |
        v                v
   prometheus  <----  tempo
   (scrape :8889)       (otlp :4317)
        |                |
        +-----> grafana (datasources: prometheus, tempo; :3000)
```
No component is reachable from the public internet.

### Service/container list
- `otel-collector` (contrib)
- `prometheus`
- `tempo`
- `grafana`

### Networks / volumes
- Internal Docker network (or LAN VLAN). Volumes: `prometheus-data`, `tempo-data`, `grafana-data` (or host bind mounts).

### Exposed ports (internal/LAN only)
- Collector: 4317, 4318, 13133 (health).
- Prometheus: 9090 (internal).
- Tempo: 3200 (internal), 4317 (from collector).
- Grafana: 3000 (admin/Viewer, TLS).

### Health checks
- Collector `/health` (13133); Prometheus `/-/healthy`; Tempo `/ready`; Grafana `/api/health`.

### Startup order / restart
- Collector first (health gate), then Prometheus/Tempo, then Grafana. `restart: unless-stopped`.

### Resource limits
- Collector: 512 MB; Prometheus: 1 GB; Tempo: 1 GB; Grafana: 512 MB (conservative; tune in 33.6).

### Log rotation
- Container logs rotated (max-size 10m, max-file 3); Grafana/Prometheus native.

### Update strategy
- Pin image versions in compose; rolling restart per component; Collector config change requires health re-check.

### Primary deployment choice (§17)
**Separate Linux observability host** running the stack via **Docker Compose** (Phase 33.2), reachable from the Windows Service over the LAN. Rationale: keeps the Windows route-authority host lean and isolated; Compose is the simplest reproducible deployment; Windows-native OTel binaries are an alternative but mix the critical host with observability load. Fallback: same-Windows-host Docker / Windows-native services if a separate host is not permitted.

## 18. Environment separation

| Aspect | Development | Test | Production |
|--------|-------------|------|------------|
| App telemetry | console exporter allowed; local Collector | disabled by default; in-memory exporters in tests | OTLP → Collector (TLS+auth) |
| Collector | local, loopback, debug exporter on | not required (tests self-contained) | LAN, TLS, auth, no debug |
| Sampling | 100% | n/a (disabled) | 25% |
| Retention | 1–3d traces, short metrics | n/a | 7–14d traces, 30d metrics |
| Console exporter | enabled (Dev only) | off | off |
| External backend | none | none | Collector only |
| Alerts | off | off | on |

Storage namespaces are separated by `deployment.environment.name` (a resource attribute already emitted); no two environments share a metrics/traces namespace without that filter.

## 19. Required runbooks (Phase 33.2–33.4 to author)

Each: symptoms → dashboard/query → likely causes → safe checks → corrective actions → escalation → evidence to preserve.

1. **Collector unavailable** — `up{job="otel-collector"}==0`; app unaffected (verified by 32.9); restart container; check 13133 health; evidence: Collector logs.
2. **No metrics arriving** — Grafana blank; check Collector `prometheus` exporter :8889 + Prometheus target health; app `Enabled=true`; evidence: scrape config.
3. **No traces arriving** — Tempo search empty; check Collector→Tempo :4317; sampling ratio; evidence: Collector debug (dev only).
4. **High route-operation failures** — Dashboard 2 / `RouteOpFailureRatio`; check VPN/DNS reachability; evidence: trace links.
5. **High runtime latency** — `cycle_duration_p95`; check host load; evidence: execution vs planning split.
6. **Prefix-check failures** — Dashboard 3; check official prefix endpoint reachability; evidence: trace.
7. **DNS failures** — `DnsLookupFailures`; check resolver; evidence: trace `cache_state`.
8. **IPC timeouts** — `IpcTimeoutRatio`; check named-pipe/SCM; evidence: trace.
9. **Disk full** — `PrometheusDiskNearFull`; raise retention or expand volume; evidence: df.
10. **Grafana unavailable** — :3000 down; restart; check provisioning mount; evidence: Grafana log.
11. **Tempo unavailable** — traces 503; Collector buffers bounded then drops; restart Tempo; evidence: Tempo log.
12. **Certificate/auth failure** — OTLP TLS/mTLS reject; renew cert; check `HeadersEnvironmentVariable`; evidence: Collector auth log.
13. **Safe restart** — drain (SIGTERM); verify health before resuming; never restart during critical repair window without notice.
14. **Upgrade/rollback** — pin versions; rollback = previous compose tag + git config; verify health + one cycle metric.

## 20. Failure isolation (consumption is not a runtime dependency)

Confirmed against Phase 32.9 behavior and this design:
- **Service continues if Collector down:** app export is fire-and-forget via SDK queue; exporter errors isolated (Phase 32.9 integration test proves business ops unaffected). ✅
- **Collector buffering bounded:** `sending_queue` + `retry_on_failure` with `max_elapsed_time`; drops after bound, never OOMs host (memory_limiter). ✅
- **Prometheus down ≠ Collector trace export stops:** traces go to Tempo independently. ✅
- **Tempo down ≠ metrics lost:** metrics go to Prometheus independently. ✅
- **Grafana down ≠ ingestion affected:** Grafana is purely a read UI. ✅
- **Alert failure ≠ collection affected:** Alertmanager is downstream of Prometheus; alert pipeline failure never blocks scrape/export. ✅
- **No infra component blocks route reconciliation:** the only app dependency is the SDK's bounded export queue, which never blocks business calls (proven). ✅

**Failure domains:** App→Collector (network/queue, bounded); Collector→Tempo (queue, bounded); Collector→Prometheus (scrape pull, decoupled); Prometheus→Grafana (pull); Alertmanager (isolated).

## 21. Implementation roadmap

### Phase 33.2 — Local observability stack
- **Files:** `docker-compose.yml` (collector, prometheus, tempo, grafana), `otel-collector.yaml`, `prometheus.yml`, `tempo.yaml`, Grafana provisioning (datasources).
- **Infra:** Docker Compose on Linux observability host (or Windows host).
- **Tests:** smoke test — Service → Collector → Prometheus/Tempo → Grafana renders one cycle metric + one trace.
- **Risks:** port/firewall, TLS certs.
- **Gate:** one runtime cycle visible end-to-end in Grafana.
- **Commit:** `feat(observability): add local observability stack (collector/prometheus/tempo/grafana)`

### Phase 33.3 — Dashboards and recording rules
- **Files:** Grafana dashboard JSON (5 dashboards), `recording_rules.yml`.
- **Infra:** none new.
- **Tests:** dashboard provisioning loads; recording-rule evaluation returns finite values.
- **Risks:** panel uses unavailable metric (guarded by §10 inventory).
- **Gate:** all 5 dashboards render with live data; recording rules compute.
- **Commit:** `feat(observability): add Grafana dashboards and Prometheus recording rules`

### Phase 33.4 — Alerts and runbooks
- **Files:** `alert_rules.yml`, `alertmanager.yml` (secret refs), runbook docs.
- **Infra:** Alertmanager (or Grafana unified alerting).
- **Tests:** alert rule syntax valid; synthetic failure triggers alert in dev.
- **Risks:** false positives (mitigated by `for`); alert storm on Collector down (inhibition).
- **Gate:** planned synthetic failure raises expected alert; no storm.
- **Commit:** `feat(observability): add alert rules, notification routing, and runbooks`

### Phase 33.5 — Production hardening
- **Files:** TLS/mTLS config, secret-management refs, remote-storage decision doc.
- **Infra:** certs, secret store, optional object storage for Tempo.
- **Tests:** TLS/auth failure rejects pushes; retention/sizing validated.
- **Risks:** cert expiry, secret rotation.
- **Gate:** only authenticated OTLP accepted; retention within disk budget.
- **Commit:** `feat(observability): harden observability stack (TLS, secrets, retention)`

### Phase 33.6 — End-to-end validation
- **Files:** validation report; load/retention test harness (optional).
- **Infra:** as deployed.
- **Tests:** collector-unavailable test (app still reconciles), load/retention validation, operational acceptance.
- **Risks:** underestimated volume (revisit §15 sizing).
- **Gate:** full flow proven; failure isolation re-verified; acceptance signed off.
- **Commit:** `docs(observability): validate telemetry consumption platform end-to-end`

## 22. Verification (this phase is documentation-only)

Run per §22 of the task: `dotnet build`, `dotnet test`, Observability filter, Service tests, Stress. No source/config/test files change — only docs. See §24 for the changed-files check.

## 23. Summary of design decisions
- Stack: Collector + Prometheus + Tempo + Grafana (self-hosted, offline-capable, CNCF-standard).
- Collector is the sole app-facing OTLP boundary (TLS + auth); backends never directly reachable by the app.
- Metrics unsampled; traces head-sampled at 25% prod / 100% dev.
- No logs/Loki (no log telemetry contract).
- Privacy preserved: only approved bounded tags + 3 resource attrs stored.
- Failure isolation inherited from Phase 32.9; consumption never blocks route reconciliation.
- Config-as-code; telemetry data disposable.
