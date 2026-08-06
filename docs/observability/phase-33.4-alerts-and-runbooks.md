# Phase 33.4 — Alert Rules, Alertmanager, and Operational Runbooks

**Status:** Complete (uncommitted). Builds on Phase 33.3 (dashboards + recording rules).
**Scope:** Infrastructure and documentation only. No application telemetry, spans,
metrics, tags, gauges, or OpenTelemetry hosting changed. No application source
touched.
**Stack:** Five services — `otel-collector`, `prometheus`, `tempo`, `grafana`,
`alertmanager` (this phase adds the fifth, Alertmanager).

---

## 1. What this phase adds

- **Alertmanager** as the fifth Compose service (image `prom/alertmanager:v0.27.0`,
  matching the existing Prometheus `v2.53.0` release line), with a read-only
  config mount, a loopback-only host port, a health check, restart policy, and a
  persistent `alertmanager-data` volume.
- **Alertmanager base configuration** (`alertmanager/alertmanager.yml`): a root
  route that groups by `alertname` / `deployment_environment_name` / `severity`,
  with `group_wait: 30s`, `group_interval: 5m`, `repeat_interval: 4h`, three
  inhibition rules, and a **no-op `null` receiver** (delivers nothing). A reusable
  notification template (`templates/default.tmpl`) ships for future production
  receivers (Phase 33.5) but is not referenced by the default config.
- **Prometheus → Alertmanager wiring** in `prometheus.yml`: an `alerting:` block
  pointing at `alertmanager:9093` (internal network). Recording-rule config and
  scrape behaviour are unchanged. Three new scrape jobs were added so
  infrastructure alerts are backed by real metrics: `tempo` (`:3200/metrics`),
  `grafana` (`:3000/metrics`), and `alertmanager` (`:9093/metrics`).
- **15 Prometheus alert rules** (`prometheus/rules/irandirect-alert-rules.yml`)
  in group `irandirect_alerts`: 9 application + 6 infrastructure. Every alert
  carries `severity`, `component`, `service` labels and `summary`, `description`,
  `runbook_url`, `dashboard_url` annotations. Names carry the `IranDirect`
  prefix.
- **17 runbooks** under `runbooks/` — one per alert plus two procedure runbooks
  (`safe-restart.md`, `upgrade-and-rollback.md`) plus a documented coverage-gap
  runbook for `prometheus-storage-pressure`. Every runbook follows the required
  standard (meaning, impact, dashboard+query, symptoms, causes, safe checks,
  corrective actions, what-not-to-do, escalation, evidence, verification,
  related alerts, ownership). No secrets, route identities, prefixes, domains,
  payloads, file paths, or exception text.
- **Dashboard integration**: each of the five Phase 33.3 dashboards gains an
  "Alert coverage & runbooks" text panel and an Alertmanager dashboard link.
  Alert *truth* stays in Prometheus; Grafana-managed alerting was not added.
- **Notification-secret model**: `.env.example` gains five
  `ALERTMANAGER_*` placeholder keys (SMTP/webhook/PagerDuty). No values are
  committed; the local config delivers nothing. Secret-bearing production
  routing is deferred to Phase 33.5.
- **Validation**: promtool `check config`/`check rules` (20 recording + 15 alert
  rules), amtool `check-config` (3 inhibit rules, 1 receiver), live firing +
  delivery + resolution of `IranDirectCollectorUnavailable`, failure-isolation
  checks, and a static-deliverable script.

---

## 2. Live-verified facts (from the running stack)

- Recording-rule group `irandirect_recording`: **20 rules** (`/api/v1/rules`).
- Alert-rule group `irandirect_alerts`: **15 rules** (`/api/v1/rules`).
- Only `up` targets at the time of inventory: `prometheus` and `otel-collector`.
  This phase adds `tempo`, `grafana`, `alertmanager` scrape jobs, so those
  targets now exist too.
- No recorded **failure-ratio** recording rule exists (Phase 33.3 confirmed this).
  DNS/IPC/support/route-operation failure ratios are therefore derived inline in
  the alert expressions from committed counters with an `outcome!="success"`
  filter and a denominator `> 0` guard.
- `service.enabled` gauge and `prefix.consecutive_failures` gauge are **not
  instrumented** (Phase 33.3 §2.3). The corresponding alerts use conservative
  absence/outcome-rate logic and document the gap.

---

## 3. Alert inventory (15)

### Application (9)

| Alert | Severity | Expression basis | `for` |
|---|---|---|---|
| `IranDirectServiceTelemetryAbsent` | warning | runtime-cycle rate ≈ 0 ∧ last cycle > 15m ago | 15m |
| `IranDirectRuntimeCycleFailureRateHigh` | warning | `rate(cycles_failed)/rate(cycles_started) > 0.10` ∧ volume guard | 10m |
| `IranDirectRuntimeCycleFailureRateCritical` | critical | `> 0.30` ∧ volume guard | 5m |
| `IranDirectRuntimeCycleLatencyHigh` | warning | `runtime_cycle_duration_p95_5m > 5000` ms | 15m |
| `IranDirectRouteOperationFailureRatioHigh` | warning | `rate(routes_ops_failed)/rate(routes_ops_requested) > 0.05` ∧ volume guard | 10m |
| `IranDirectPrefixChecksFailing` | warning | `rate(prefix_checks{outcome!="success"})/rate(prefix_checks) > 0.20` ∧ volume guard | 30m |
| `IranDirectDnsLookupFailureRatioHigh` | warning | `rate(dns_lookups{outcome!="success"})/rate(dns_lookups) > 0.20` ∧ volume guard | 10m |
| `IranDirectIpcTimeoutRatioHigh` | warning | `rate(ipc_requests{outcome="timeout"})/rate(ipc_requests) > 0.05` ∧ volume guard | 10m |
| `IranDirectSupportExportFailures` | warning | `increase(support_bundles_failed[30m]) >= 2` | 0m |

### Infrastructure (6)

| Alert | Severity | Expression basis | `for` |
|---|---|---|---|
| `IranDirectCollectorUnavailable` | critical | `up{job="otel-collector"} == 0` | 2m |
| `IranDirectPrometheusTargetDown` | warning | `up{job=~"tempo|grafana|alertmanager"} == 0` | 2m |
| `IranDirectAlertmanagerUnavailable` | warning | `up{job="alertmanager"} == 0` | 2m |
| `IranDirectTempoUnavailable` | warning | `up{job="tempo"} == 0` | 2m |
| `IranDirectGrafanaUnavailable` | warning | `up{job="grafana"} == 0` | 2m |
| `IranDirectCollectCertificateOrAuthFailure` | warning | cycle telemetry stalled while collector `up==1` (best-effort auth heuristic) | 10m |

---

## 4. Severity model

- **info** — dashboard/UI only; no notification. (No alerts currently emit info.)
- **warning** — business-hours notification in a future production override;
  local stack: visible in Alertmanager UI/API only.
- **critical** — immediate future production notification; local stack: visible
  only (no-op receiver).

Local stack routes every severity to the `null` receiver. Critical alerts use a
shorter `repeat_interval: 1h` via the severity sub-route.

---

## 5. Grouping and routing

Root route `group_by: [alertname, deployment_environment_name, severity]`.
Severity sub-routes (`critical` → 1h repeat, `warning`, `info`) all terminate at
the `null` receiver. No external notification is sent by the default config.

---

## 6. Inhibition rules (3, all documented)

1. **CollectorUnavailable → application alerts.** When the collector is down,
   `IranDirectServiceTelemetryAbsent` and any service-component alert with
   `component="service"` is suppressed, because the missing telemetry makes them
   unreliable. (Live: `IranDirectCollectorUnavailable` fired while the collector
   was stopped; the inhibit rule is loaded — see §8.)
2. **PrometheusTargetDown → its derivative alerts.** A down target suppresses
   alerts that depend on that target's health signal.
3. **critical → warning (same alertname).** The critical variant suppresses the
   matching warning variant so operators see one entry, not both.

Does not over-inhibit: `AlertmanagerUnavailable` does **not** silence
application alerts in Prometheus (alerts still evaluate; only delivery stops).

---

## 7. Notification-secret strategy

- The local default delivers nowhere (`null` receiver). Alerts are inspected via
  the Alertmanager UI/API (`http://localhost:9095`) or Prometheus → Alerts.
- `.env.example` lists five `ALERTMANAGER_*` placeholder keys for future
  production receivers (SMTP, webhook, PagerDuty). They are empty by default and
  never hold real values in the repo.
- No env-var substitution is performed on `alertmanager.yml` in this phase (it
  would require a fragile startup templating step). To enable a real receiver in
  production (Phase 33.5), set the matching `.env` variable and add a receiver +
  route to `alertmanager.yml` referencing it.

---

## 8. Validation results

- `promtool check config /etc/prometheus/prometheus.yml` → SUCCESS (20 recording
  + 15 alert rules referenced via `rule_files`).
- `promtool check rules irandirect-alert-rules.yml` → SUCCESS: 15 rules found.
- `amtool check-config /etc/alertmanager/alertmanager.yml` → SUCCESS (1 receiver,
  **3 inhibit rules**).
- Prometheus `/api/v1/rules`: groups `irandirect_recording` (20) +
  `irandirect_alerts` (15).
- Prometheus `/api/v1/alertmanagers`: active `http://alertmanager:9093/...`.
- Static deliverable script: 11/11 checks (5 services, healthchecks, loopback
  port, 15 unique `IranDirect*` alerts with required labels/annotations,
  runbook URLs resolve, no literal credential values, `.env` ignored,
  `.env.example` placeholders present, dashboards parse+editable=false+linked,
  scope clean).

### Live firing / resolution test

- Stopped `otel-collector`. After `for: 2m`, **`IranDirectCollectorUnavailable`
  fired** (critical) in Prometheus and appeared in Alertmanager (1 active alert).
  No other alert fired.
- Restarted `otel-collector`. Within the resolution window the alert cleared:
  **0 active alerts** in both Prometheus and Alertmanager.

### Failure-isolation evidence

- **Alertmanager stopped:** Prometheus kept evaluating all 15 alert rules and
  continued scraping the collector (`up=1`); `/api/v1/query` returned HTTP 200.
  Alertmanager returned healthy on restart.
- **Collector stopped:** the independently-installed `IranDirect.Service`
  (Windows Service, PID 4696) remained running (≈109 MB) — collector outage does
  not crash the Service.
- Architecturally guaranteed: Prometheus→Alertmanager is one-way; Tempo, Grafana,
  and the collector have no dependency on Alertmanager; the Service exports to the
  collector, independent of every other backend.

---

## 9. Known coverage gaps (explicit non-goals of this phase)

- **`IranDirectPrometheusStoragePressure` is NOT implemented.** No
  container/host filesystem-free Prometheus storage metric is available without
  node-exporter (out of scope here). `runbooks/prometheus-storage-pressure.md`
  documents the gap and the Phase 33.5 plan (node-exporter / infra monitoring).
- **`service.enabled` and `prefix.consecutive_failures` gauges are absent**, so
  `ServiceTelemetryAbsent` uses a conservative cycle-activity rule and
  `PrefixChecksFailing` uses the outcome rate; both documented.
- **Failure-ratio recording rules do not exist**, so DNS/IPC/route-op/support
  failure ratios are derived inline in the alert expressions (guarded).
- **No Loki / Alloy / Jaeger / Zipkin / Elasticsearch / ClickHouse** were added.
- **No PagerDuty/email/webhook delivery** is configured; the local stack is
  no-op by design.

---

## 10. How to operate

- **Inspect Prometheus alerts:** Prometheus → Alerts (`http://localhost:9090`),
  or `curl 'http://localhost:9090/api/v1/alerts'`.
- **Inspect Alertmanager:** UI/API at `http://localhost:9095` (loopback only).
  `curl 'http://localhost:9095/api/v2/alerts'`.
- **Silence safely:** in the Alertmanager UI, open the firing alert → Silence →
  set a comment + duration → Create. Never disable the alert rule to silence.
- **Verify firing/resolution:** stop a backend (`docker compose stop <svc>`),
  watch the matching `IranDirect<X>Unavailable` fire after its `for:` window,
  then `docker compose start <svc>` and confirm it resolves.

---

## 11. Phase 33.5 next scope

Production notification delivery: SMTP/email, webhook, and PagerDuty receivers
using the `ALERTMANAGER_*` env placeholders; env-var expansion in
`alertmanager.yml` (robust, tested templating); severity→receiver routing for
business-hours vs immediate paging; node-exporter / infra disk-pressure
monitoring to close the storage-pressure gap; and a Grafana "alerts" view if
desired (alert truth remains Prometheus-side).
