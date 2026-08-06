# Phase 33.3 — Grafana dashboards and Prometheus recording rules

Phase 33.3 adds version-controlled Grafana dashboards and Prometheus
recording rules for the telemetry already emitted by `IranDirect.Service`
and consumed by the Phase 33.2 local observability stack.

This phase is **infrastructure/configuration only**:

- no application telemetry was added, renamed, or removed;
- no spans, metrics, or tags were added;
- no OpenTelemetry hosting was changed;
- no alerts or notification integrations were added;
- no Loki or log ingestion was added.

The only application-facing change is a single one-line pipeline-output toggle
in the collector (see §2.1).

---

## 1. Scope and deliverables

| Area | File | Notes |
|------|------|-------|
| Recording rules | `deployment/observability/prometheus/rules/irandirect-recording-rules.yml` | one group, `irandirect_recording`, 30s interval, 20 rules |
| Prometheus config | `deployment/observability/prometheus/prometheus.yml` | adds `rule_files` |
| Dashboard provisioning | `deployment/observability/grafana/provisioning/dashboards/dashboards.yaml` | one provider, folder `IranDirect`, read-only JSON |
| Dashboards (5) | `deployment/observability/grafana/dashboards/*.json` | UIDs below |
| Collector | `deployment/observability/collector/otel-collector.yaml` | `resource_to_telemetry_conversion` enabled |
| Compose | `deployment/observability/docker-compose.yml` | two new read-only mounts |
| Docs | `docs/observability/phase-33.3-*.md`, both READMEs | this document |

Everything lives under `deployment/observability/` and `docs/observability/`.
No `IranDirect.Core/`, `IranDirect.Service/`, or other source was touched.

---

## 2. Metric-name translation findings

The Phase 32 OTel instruments are named `irandirect.*` with a `.` separator.
The OpenTelemetry Collector's Prometheus exporter translates `.` to `_` and
appends the unit-derived suffix. **Names were never assumed** — they were read
from the live Prometheus instance during verification.

### 2.1 Required collector change: resource → label conversion

The stock Phase 33.2 collector exporter had:

```yaml
prometheus:
  endpoint: 0.0.0.0:8889
  resource_to_telemetry_conversion:
    enabled: false
```

With `enabled: false`, OTel **resource** attributes (which carry
`service.name`, `service.version`, `deployment.environment.name`) are dropped
at the Prometheus boundary. The dashboards and recording rules require
`deployment_environment_name` (and `service_name`) to filter by environment,
so this phase flips it to `enabled: true`.

This is a **pipeline-output** concern only. It does **not** rename, mutate, or
add any application span/metric/tag — the raw metric names and their own
instrumentation labels are untouched. The telemetry *contract* is unchanged.
Without this change the spec's required `deployment_environment_name`
filtering is impossible.

### 2.2 Exported metric inventory (verified live)

Counters (suffix `_total`), each confirmed in Prometheus:

| Contract name | Exported name |
|---------------|---------------|
| `irandirect.runtime.cycles.started` | `irandirect_runtime_cycles_started_total` |
| `irandirect.runtime.cycles.completed` | `irandirect_runtime_cycles_completed_total` |
| `irandirect.runtime.cycles.cancelled` | `irandirect_runtime_cycles_cancelled_total` |
| `irandirect.routes.operations.requested` | `irandirect_routes_operations_requested_total` |
| `irandirect.routes.operations.succeeded` | `irandirect_routes_operations_succeeded_total` |

Histograms carry an **explicit `ms` unit**, which the exporter renders as a
`_milliseconds` base suffix (the duration histograms do **not** use `_seconds`):

| Contract name | Bucket base exported as |
|---------------|-------------------------|
| `irandirect.runtime.cycle.duration` | `irandirect_runtime_cycle_duration_milliseconds_*` |
| `irandirect.runtime.planning.duration` | `irandirect_runtime_planning_duration_milliseconds_*` |
| `irandirect.runtime.execution.duration` | `irandirect_runtime_execution_duration_milliseconds_*` |
| `irandirect.routes.system_call.duration` | `irandirect_routes_system_call_duration_milliseconds_*` |

Two **count** histograms (no duration, unit `1`) keep the plain `_bucket`
suffix without `_milliseconds`:

| Contract name | Bucket base exported as |
|---------------|-------------------------|
| `irandirect.runtime.changed_routes` | `irandirect_runtime_changed_routes_*` |
| `irandirect.runtime.operations.per_cycle` | `irandirect_runtime_operations_per_cycle_*` |

For every histogram family the exporter emits `_bucket`, `_sum`, and `_count`.

### 2.3 Metrics named in the contract but **not exported**

The Phase 32 contract reserves these names, but verification proved they are
**not produced** by the running Service:

- **No instrument exists at all** (the name appears only in
  `IranDirectMetricNames.cs`, never passed to `CreateCounter`/`CreateHistogram`/
  `CreateObservableGauge`): all six gauges — `service.enabled`,
  `runtime.worker.active`, `prefix.known_count`, `routes.inventory_count`,
  `dns.cache_record_count`, `prefix.consecutive_failures` — and the two repair
  counters `runtime.repairs.with_changes` / `runtime.repairs.no_changes`, and
  `runtime.observe.duration`.
- **Instrumented but never observed** in the sampled window (the code path did
  not execute): `runtime.cycles.failed`, `routes.operations.failed`,
  `prefix.checks`, `dns.lookups`, `ipc.requests`,
  `support.bundles.exported`, `support.bundles.failed`, and the
  `prefix.check.duration` / `dns.lookup.duration` / `ipc.request.duration` /
  `support.bundle.duration` histograms.

Consequently **no dashboard or recording rule references any of these names**.
Where the brief asks for panels on them, the panel was **deliberately omitted**
rather than shown as a misleading zero; the dashboards carry a "coverage notes"
panel explaining this. A stale `irandirect_runtime_cycle_duration_count` series
(8 stale samples, 30d+ old) also exists from an earlier name; the live metric is
`..._duration_milliseconds_count` (11 current samples).

### 2.4 Duration units

Every emitted duration histogram uses unit `ms` → Prometheus suffix
`_milliseconds`. All latency panels and quantile recording rules therefore
report **milliseconds** (`unit: "ms"` in Grafana). No `_seconds` duration metric
exists.

---

## 3. Label inventory

Labels observed on live `irandirect_*` series:

| Label | Source | Used by |
|-------|--------|---------|
| `deployment_environment_name` | OTel resource (now promoted via §2.1) | filtering everywhere |
| `service_name` | OTel resource (now promoted) | filtering / context |
| `operation` | instrument label | runtime, route, IPC, support panels |
| `outcome` | instrument label | outcome breakdowns |
| `trigger` | instrument label | cycle start-rate breakdown |
| `instance`, `job`, `exported_job`, `job_source`, `le` | Prometheus internals | not surfaced |

### 3.1 Labels that do **not** exist

The following labels are referenced by the Phase 33.3 brief but were **absent**
from every live series and so appear in **no** query:

- `failure_category` — defined in `IranDirectTagNames.cs` but never attached to
  any metric that reaches Prometheus.
- `route_kind`, `change_kind`, `source`, `cache_state`, `ipc_command` — all
  defined but not present on any emitted metric series in the sampled window.
- `service_version` — not set by the Service, so it never reaches Prometheus;
  the dashboards therefore expose **no** service-version variable.

### 3.2 Privacy / prohibited-label scan

A scan of **all** Prometheus label names and of the labels carried by
`irandirect_*` series returned **zero** prohibited labels. Concretely absent:
`destination_prefix`, `gateway`, `next_hop`, `interface`, `domain`, `url`,
`file_path`, `pipe_payload`, `machine_name`, `user_name`, `exception_message`,
`route_identity`, `execution-step_identity`. The only `endpoint` label in
Prometheus belongs to Prometheus' own Consul SD internals
(`prometheus_sd_consul_rpc_duration_seconds`), never to IranDirect metrics.

---

## 4. Recording rules

Group `irandirect_recording`, evaluation interval **30s** (superset of the
global 15s). All expressions were checked with `promtool check rules` and
verified to produce series against live telemetry.

Naming convention: `irandirect:<subject>_<aggregation><window>`
(e.g. `irandirect:runtime_cycle_rate5m`,
`irandirect:runtime_cycle_success_ratio5m`,
`irandirect:runtime_cycle_duration_p95_5m`).

Grouping keeps the bounded dimensions dashboards filter on
(`deployment_environment_name`, `service_name`) plus the metric's own labels;
`instance`/`job` are aggregated away.

| Recorded series | Expression (summary) |
|-----------------|----------------------|
| `irandirect:runtime_cycle_rate5m` | `sum by (env, svc, trigger) rate(irandirect_runtime_cycles_started_total[5m])` |
| `irandirect:runtime_cycle_completed_rate5m` | `sum by (env, svc, outcome) rate(irandirect_runtime_cycles_completed_total[5m])` |
| `irandirect:runtime_cycle_cancelled_rate5m` | `sum by (env, svc) rate(irandirect_runtime_cycles_cancelled_total[5m])` |
| `irandirect:runtime_cycle_completed_rate5m:total` | total completion rate (denominator for ratios) |
| `irandirect:runtime_cycle_success_ratio5m` | completed / started, guarded `> 0` |
| `irandirect:runtime_cycle_cancelled_ratio5m` | cancelled / started, guarded `> 0` |
| `irandirect:runtime_cycle_no_change_ratio5m` | completed{outcome=no_change} / completed, guarded `> 0` |
| `irandirect:runtime_cycle_duration_p50_5m` | `histogram_quantile(0.50, sum by (le, env, svc) rate(..._milliseconds_bucket[5m]))` |
| `irandirect:runtime_cycle_duration_p95_5m` | p95 quantile |
| `irandirect:runtime_cycle_duration_p99_5m` | p99 quantile |
| `irandirect:runtime_cycle_duration_mean5m` | `rate(_sum[5m]) / rate(_count[5m]) > 0` |
| `irandirect:runtime_planning_duration_p95_5m` | planning p95 quantile |
| `irandirect:runtime_execution_duration_p95_5m` | execution p95 quantile |
| `irandirect:routes_system_call_duration_p95_5m` | route syscall p95 by `operation` |
| `irandirect:runtime_changed_routes_p95_5m` | changed-routes p95 quantile |
| `irandirect:runtime_operations_per_cycle_p95_5m` | operations-per-cycle p95 quantile |
| `irandirect:routes_operations_requested_rate5m` | route ops requested by `operation` |
| `irandirect:routes_operations_succeeded_rate5m` | route ops succeeded by `operation` |
| `irandirect:routes_operation_success_ratio5m` | succeeded / requested by `operation`, guarded |
| `irandirect:routes_operation_success_ratio5m:total` | succeeded / requested total, guarded |

**Division guards.** Every ratio divides by `(rate(...) > 0)` so the rule
records **no sample** (not NaN/+Inf) when the Service is idle. Quantiles use
canonical `histogram_quantile` over `sum by (le, …) rate(_bucket[5m])` — bucket
values are never averaged. The mean uses `rate(_sum)/rate(_count)`, never an
average of bucket midpoints.

**Omitted ratios.** A dedicated `routes_operation_failure_ratio5m` and a
`dns_lookup_failure_ratio5m` were **not** added: `routes.operations.failed` and
`dns.lookups` carry no verified `outcome` label split in the sampled window, so
a failure ratio would be unverifiable. The dashboards instead derive failure
rates inline from `outcome!="success"` filters, which were confirmed valid.

---

## 5. Dashboards

All five share: `schemaVersion: 39`, `version: 1`, `editable: false`,
`editable` enforced by provider `allowUiUpdates: false`, folder **IranDirect**,
refresh **30s**, default range **last 6h**, provisioned datasource UIDs
(`irandirect-prometheus`, `irandirect-tempo`), and a Tempo "Explore" link.
Panel IDs are unique within each dashboard; titles are unique within each
dashboard.

| UID | Title | Panels |
|-----|-------|--------|
| `irandirect-service-overview` | IranDirect / Service Overview | 19 |
| `irandirect-runtime-reconciliation` | IranDirect / Runtime Reconciliation | 21 |
| `irandirect-prefix-dns` | IranDirect / Prefix and DNS | 13 |
| `irandirect-ipc-support` | IranDirect / IPC and Support Export | 18 |
| `irandirect-reliability-errors` | IranDirect / Reliability and Errors | 21 |

### 5.1 Service Overview (`irandirect-service-overview`)

Variables: `env` (deployment_environment_name, multi + All).

- Telemetry last seen (stat, seconds since last cycle sample)
- Cycle start rate (stat, reqps)
- Cycle success ratio (stat, percent)
- Cancellation ratio (stat, percent)
- Runtime cycles started by trigger (time series)
- Runtime cycles completed by outcome (time series)
- Cycle success / no-change ratio (time series)
- Cycle cancellations (time series)
- Runtime cycle duration p50/p95/p99 (time series, ms)
- Mean cycle duration (stat, ms)
- Changed routes per cycle p95 (time series)
- Operations per cycle p95 (time series)
- Route operation success ratio (stat, percent)
- Coverage notes (text: why the gauge panels are absent)

### 5.2 Runtime Reconciliation (`irandirect-runtime-reconciliation`)

- Cycle duration p50/p95/p99 (time series)
- Phase latency p95: planning vs execution (time series)
- Planning p95 / Execution p95 / Cycle p95 (stats)
- Route system-call p95 (stat)
- Changed routes per cycle p95 (time series)
- Operations per cycle p95 (time series)
- Route operations requested by operation (time series)
- Route operations succeeded by operation (time series)
- Route operation success ratio by operation (time series)
- Route operation failures (time series)
- Route system-call latency p95 by operation (time series)
- Cycle outcomes over time (time series)
- No-change ratio (time series; substitute for un-instrumented repair counters)
- Coverage notes (text)

### 5.3 Prefix and DNS (`irandirect-prefix-dns`)

- Prefix check rate by outcome / by operation (time series)
- Prefix check latency p50/p95/p99 (time series, ms)
- Prefix check mean latency (stat)
- DNS lookup rate by outcome / by operation (time series)
- DNS lookup latency p50/p95/p99 (time series, ms)
- DNS mean latency (stat)
- DNS lookup failure ratio (time series)
- Coverage notes (text: instrumented-but-inactive metrics; no cache-state panel)

### 5.4 IPC and Support (`irandirect-ipc-support`)

- IPC request rate / success ratio / timeout ratio / p95 latency (stats)
- IPC requests by outcome / by command (time series)
- IPC latency p50/p95/p99 (time series)
- IPC timeout and failure rate (time series)
- Support exports succeeded / failed / failure ratio / p95 (stats)
- Support exports over time (time series)
- Support export latency p95 by operation (time series)
- Coverage notes (text: privacy — no command payload/path exposed)

### 5.5 Reliability and Errors (`irandirect-reliability-errors`)

Triage only — **no alert rules**.

- Cycle failure rate / route operation failure rate / cancellation rate /
  telemetry-last-seen (stats)
- Runtime cycle failures and cancellations (time series)
- Route operation failures by operation (time series)
- DNS lookup failures / IPC failures and timeouts / support export failures
  (time series)
- p99 latency across operations (time series)
- Prometheus targets up / collector scrape health / scrape duration /
  series scraped (stats + time series)
- Coverage notes (text: `failure_category` absent; Tempo not queryable from
  Prometheus here)

---

## 6. Variables and filtering

- `env` — `label_values(irandirect_runtime_cycles_started_total,
  deployment_environment_name)`, multi-select with `(.*)` All, refresh on time
  range change. Every query filters on
  `deployment_environment_name=~"$env"`.
- No `service_version` variable (the Service does not emit `service.version`,
  verified §3.1).
- No `outcome` dashboard variable; outcome is used only inside per-panel
  `by (outcome)` groupings and `outcome!="success"` failure filters.

---

## 7. Trace-link decision

**No metric-to-trace exemplars are configured, and none are fabricated.**
The Phase 32/33.2 application instrumentation does not emit Prometheus
exemplars, and the brief forbids modifying application instrumentation merely
to add them. A fabricated trace-ID label in metrics would violate the frozen
telemetry contract.

Instead each dashboard carries a **link** to Grafana Explore pre-targeted at
the Tempo datasource, filtered by `service.name = IranDirect.Service` and the
current time range. From there an operator can pivot to traces manually.

`failure_category` cannot be used to correlate either: it is not present on any
live metric (§3.1).

---

## 8. Validation results

### 8.1 Prometheus

```
promtool check config /etc/prometheus/prometheus.yml
  SUCCESS: 1 rule files found
  SUCCESS: ... valid prometheus config file syntax
promtool check rules /etc/prometheus/rules/irandirect-recording-rules.yml
  SUCCESS: 20 rules found
```

### 8.2 Live rule evaluation

After the Service emitted telemetry, `/api/v1/rules` reported group
`irandirect_recording`, interval `30s`, **20 rules, 0 unhealthy**. Querying the
recorded series while the Service was running: **18 of 20 produced real values**
(e.g. cycle p95 ≈ 2425 ms, planning p95 ≈ 4.75 ms, success ratio = 1). The
remaining 2 (cancellation ratio/rate) were empty because no cancellation had
occurred yet — expected, and they are division-guarded so they produce no
sample rather than an error.

### 8.3 Static dashboard validation

A script parsed all five JSON files and asserted: parses, filename == UID,
`editable: false`, unique panel IDs, unique panel titles, only supported panel
types (`timeseries`, `stat`, `barchart`, `table`, `text`, `row`), no unknown
datasource UID, **no forbidden strings**, every panel query references a known
metric name (live, recorded, or documented-absent), and an `env` variable is
present. **48 checks passed, 0 failed.**

### 8.4 Live dashboard verification (Grafana API)

- `/api/search` returned exactly **5** dashboards, all in folder **IranDirect**.
- Both datasources (`irandirect-prometheus`, `irandirect-tempo`) resolve.
- Every panel query was executed through the Grafana datasource proxy:
  **83 queries, 0 errors**. 41 returned data, 42 returned valid empty results.
  The empty panels are exactly the documented not-yet-exercised families
  (IPC, prefix/DNS, and cancellation) — they return empty, never error.
- After `docker compose restart grafana`, all 5 dashboards re-provisioned in
  the IranDirect folder (restart-survival confirmed).

---

## 9. Stack health

The stack runs the same four Compose services with unchanged ports, health
checks, named volumes, and loopback-only host bindings. New read-only mounts:
`./prometheus/rules → /etc/prometheus/rules:ro` and
`./grafana/dashboards → /var/lib/grafana/dashboards:ro`. No Loki/Alloy/Jaeger/
Zipkin/Elasticsearch/ClickHouse added; Service observability stays
disabled-by-default; no appsettings or OTel package changes.

---

## 10. Known limitations

1. **Gauge panels absent.** Six contract gauges have no instrument; the
   Service Overview dashboard therefore shows no service-enabled / worker-active
   / prefix-count / route-inventory / DNS-cache / consecutive-failure panels.
   They will appear automatically once the gauges are instrumented — no
   dashboard change needed.
2. **Repair breakdown absent.** `repairs.with_changes` /
   `repairs.no_changes` are not instrumented; the "No-change ratio" panel is the
   faithful substitute.
3. **Observation latency absent.** `runtime.observe.duration` has no instrument.
4. **failure_category missing.** No live metric carries it, so failure panels
   use `outcome` instead of a category breakdown.
5. **Empty panels until exercised.** IPC, prefix/DNS, and cancellation panels
   stay empty until those code paths run; they are validated, not broken.
6. **No metric exemplars / no alerting.** Per brief; triage is via the
   Reliability dashboard only.

---

## 11. Non-goals (explicit)

- No alert rules or notification integrations (deferred to a later phase).
- No Loki / log ingestion.
- No application telemetry changes, instrument additions, or renames.
- No OpenTelemetry hosting changes beyond the collector's
  `resource_to_telemetry_conversion` toggle (§2.1), which is a pipeline-output
  setting, not an application change.
- No Tempo-as-Prometheus-datasource query (Tempo exposes no scraped metrics in
  this stack).

---

## 12. Next phase

Phase 33.4 (per the 33.1 roadmap): Prometheus **alerting** rules and
notification routing for the metrics and recording rules introduced here,
building on the dashboards in this phase.
