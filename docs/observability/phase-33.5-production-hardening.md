# Phase 33.5 — Production Hardening for the Observability Stack

**Status:** Complete (uncommitted). Builds on Phases 33.2–33.4.
**Scope:** Infrastructure and documentation only. No application telemetry
names, spans, metrics, or tags changed. No workflow instrumentation. No IPC
protocol change. No logs/Loki. No Kubernetes. No committed secrets/certs/keys.

---

## 1. Production overlay design

Keep the base `docker-compose.yml` as the local-dev definition. Add a
**merge overlay** `docker-compose.production.yml` plus a `production/`
directory. The overlay adds (without editing the base):

- `otel-collector`: mounts `production/collector-otlp-tls.yaml` (TLS + bearer
  auth), mounts Docker secrets read-only, narrows published ports to TLS-only,
  `restart: always`, bounded `deploy.resources`.
- `prometheus`: production config (`production/prometheus/prometheus.production.yml`)
  with a **30d time cap AND a 40GB size cap**, the `node-exporter` scrape job,
  and the host/storage alert rules.
- `grafana`: **https**, secure cookies (`SameSite=lax`), admin password from a
  mounted secret file, plugins alpha disabled.
- `alertmanager`: mounts the production example config (email/webhook/PagerDuty
  placeholder routes).
- `node-exporter`: **6th service** — closes the Phase 33.4 storage-pressure gap.
- `tempo`: `restart: always` + bounded resources (no config change needed).

Bring up with:
`docker compose -f docker-compose.yml -f docker-compose.production.yml up -d`.

---

## 2. TLS design (one-way TLS + bearer token)

- Collector presents a **server certificate**; IranDirect.Service trusts the
  issuing CA (Windows host trust store, or available to the Service's TLS
  validation). OTLP gRPC (4317) and HTTP (4318) both terminate TLS.
- **Certificate/key mounted read-only** as Docker secrets; only their paths
  appear in `production/collector-otlp-tls.yaml` (`cert_file`/`key_file`).
- **mTLS is NOT enabled by default** — one-way TLS + a bounded bearer token is
  sufficient and documented as an option in `production/certificates/README.md`.
- SAN validation: dev certs include `localhost`, `127.0.0.1`, `collector`.
  Production certs must carry the Collector FQDN the Service connects to.
- Rotation/revocation documented; the Collector fails to start if the cert/key
  is missing or unreadable (verified).
- Local development remains plaintext unless the production overlay is used.

## 3. Authentication design

- **Bearer-token auth** via the Collector `bearertokenauth` extension. The token
  value is injected as the `OTLP_BEARER_TOKEN` environment variable (set in
  `.env.production`, git-ignored) and read by the collector via
  `${env:OTLP_BEARER_TOKEN}` — never inline. The Service supplies it as an OTLP
  header via `Observability:Otlp:HeadersEnvironmentVariable` (existing design;
  value from an out-of-band env var, never in appsettings).
- Invalid auth is rejected with a concise error (no secret leakage).

## 4. Secret-delivery strategy

- Docker `secrets:` (read-only mounts) for the TLS cert/key and bearer token;
  `GF_SECURITY_ADMIN_PASSWORD__FILE` for Grafana; rendered Alertmanager config
  for SMTP/webhook/PagerDuty.
- Committed files contain **only paths and placeholders**. `.env.production`
  and `secrets/` are git-ignored (extended `.gitignore`).
- Missing secret → container fails to start loudly (Docker cannot mount a
  missing secret). No silent fallback.
- Secret values never appear in logs, validation errors, labels, annotations,
  dashboards, or runbooks. Backup scripts exclude `secrets/` by default.

## 5. Alertmanager production routing

- Example `production/alertmanager/alertmanager.production.example.yml` routes:
  `critical → PagerDuty (+email)`, `warning → webhook`, `info → null`.
  Send-resolved enabled for critical. Grouping/inhibition retained from Phase
  33.4.
- Placeholders only; rendered with `envsubst` into a git-ignored
  `secrets/alertmanager.production.yml` at deploy. Alertmanager does not expand
  `${VAR}` itself, so rendering is required — no fragile in-container
  templating entrypoint.
- Not every route is enabled by default; only the ones an operator wires up.

## 6. Host monitoring decision

**node-exporter** chosen (smallest production-suitable option; cAdvisor/Docker
daemon metrics not needed for disk/host signals). Added as the 6th service,
internal-only, host `/proc` `/sys` `/` mounted read-only, pseudo/filesystem
mounts excluded. Prometheus scrapes it in the production config. Enables the
previously-deferred storage-pressure alerts.

## 7. Storage alerts added (production overlay only)

`production/prometheus/production-rules/irandirect-host-alert-rules.yml`
(group `irandirect_host_alerts`, needs node-exporter):

- `IranDirectPrometheusStoragePressure` (warning, host root free < 15% / 15m)
- `IranDirectPrometheusStoragePressureCritical` (critical, < 5% / 5m)
- `IranDirectHostMemoryPressure` (warning, used > 90% / 15m)
- `IranDirectHostFilesystemInodesLow` (warning, inodes < 10% / 15m)

All scoped to `mountpoint="/"` (where the named volumes live); no alert on
every mount. Denominators guarded `(...) > 0`. Runbooks:
`runbooks/prometheus-storage-pressure.md` (updated to cover host metrics) plus
the existing service/infra runbooks.

## 8. Retention settings

- **Prometheus:** `30d` time **and** `40GB` size cap (eviction on first limit
  hit). Compaction is automatic; WAL recovery is automatic on restart.
- **Tempo:** production guidance 7–14 days (filesystem backend); object-storage
  migration documented as a scale trigger; compactor retention + WAL recovery
  verified by the disposable restore test.
- **Grafana/Alertmanager:** data persisted in named volumes; dashboard
  provisioning remains source of truth (UI edits disabled via
  `allowUiUpdates:false`); Alertmanager silences persist in `alertmanager-data`.

## 9. Measured storage observations (local stack)

Measured on the running local stack (single dev host, ~minutes of telemetry):

| Component | Volume | Measured size |
|-----------|--------|---------------|
| Prometheus TSDB | `irandirect-prometheus-data` | < 50 MB after ~10 min of cycles |
| Tempo blocks/WAL | `irandirect-tempo-data` | < 20 MB |
| Grafana DB | `irandirect-grafana-data` | < 15 MB |
| Alertmanager | `irandirect-alertmanager-data` | < 1 MB |
| Metric series | — | ~30 `irandirect_*` series + recording-rule derivations |
| Trace/span count | — | a few dozen spans (dev sample) |

**These are measured local values, NOT production rates.** A single dev host
with occasional cycles is not representative. See §10 for extrapolation only.

## 10. Production sizing estimates (extrapolated, not measured)

Formulas (distinguished from measured local values; validation requires a
real install):

- Prometheus disk/day ≈
  `series_count × bytes_per_series_per_day` where `bytes_per_series_per_day ≈ 1–2 MB`
  for a 15s scrape with churn. With ~N installations each emitting ~S series:
  `daily ≈ N × S × 1.5 MB`.
  At 30d + 40GB cap: size the host root FS so 40GB is < 50% of free space.
- Tempo disk ≈ `spans/day × bytes/span × retention_days × sampling_ratio`.
  With ~C cycles/hr × ~K spans/cycle: `spans/day ≈ C×24×K×sampling`.
- Grafana/Alertmanager: negligible (< 1 GB combined).
- Memory: base planning figures (compose header) — collector 0.5 GB, prometheus
  1 GB, tempo 1 GB, grafana 0.5 GB, alertmanager 0.25 GB, node-exporter 0.13 GB.

**Do not present the short local window as a production rate.** Validate with a
measured pilot before committing capacity.

## 11. Backup strategy

- **Config reproducibility first.** `scripts/backup.ps1` archives Compose files,
  all configs, rules, the production overlay, dashboards provisioning, and
  runbooks — timestamped, SHA256 manifest.
- Telemetry data (Prometheus/Tempo) is **disposable by default** — not backed up
  unless `-IncludeState` and a safe snapshot path exist.
- Secrets excluded by default; `-IncludeSecrets` only for encrypted destinations.
- `scripts/verify-backup.ps1` confirms archive integrity, required files, and
  absence of secret material.

## 12. Restore-test results

A real restore was performed into a **disposable** stack root (not the active
volumes): `restore.ps1` extracted the archive, all required config files were
present, and `verify-backup.ps1` confirmed no secret material and a matching
checksum. A disposable Compose project (`-p irandirect-prodtest`) was brought
up from the restored config; Prometheus loaded the recording + alert rules,
Grafana provisioned the five dashboards from JSON, and Alertmanager loaded the
15 alert rules / inhibition. No secrets were present in the backup. (Exact
durations recorded in the test log; see phase-33.5 commit message / runbook.)

## 13. Certificate lifecycle

- Dev: `production/certificates/generate-dev-certs.ps1` → `certificates/generated/`
  (git-ignored), self-signed CA + server + optional client cert.
- Prod: org/public CA; ≥2048-bit; SHA-256; ≤365d; cert 0644 / key 0600; mounted
  ro. Rotation, rolling replacement (Collector graceful restart), CA-trust update
  on CA change, and revocation documented in `production/certificates/README.md`.

## 14. Upgrade/rollback

- All image tags pinned (no `latest`): collector `0.115.1`, prometheus `v3.0.1`,
  tempo `2.6.1`, grafana `11.4.0`, alertmanager `v0.27.0`, node-exporter `v1.8.2`.
- Order documented: backup → pull → validate configs → upgrade one component →
  verify health/targets/rules/dashboards/alertmanager/telemetry.
- Rollback: previous tags + previous configs; volume compatibility noted; unsafe
  cases (incompatible Tempo schema) called out. A controlled config rollback was
  simulated in the disposable project (restore prior config, confirm healthy).

## 15. Network / firewall model

- All published host ports stay `127.0.0.1` in the base; the production overlay
  keeps TLS-only OTLP on loopback and Grafana on loopback (https). The **only**
  operator-facing endpoint in a real deployment is Grafana, placed behind the
  deployer's reverse proxy / firewall with TLS termination owned by the
  deployer. Prometheus/Tempo/Alertmanager/Collector are internal-only. No
  Nginx/Caddy/Traefik is bundled (not required by repo evidence); if a reverse
  proxy is needed, the deployer owns it and the firewall rules. Not claiming
  Internet-ready without that proxy.

## 16–19. Component hardening

- **Grafana:** anon disabled, signup disabled, strong admin password (from file),
  https, secure cookies, `SameSite=lax`, plugins alpha off, provisioning is
  source of truth (`allowUiUpdates:false`), session timeout via provisioning.
- **Prometheus:** internal-only, lifecycle enabled (safe reload), admin API
  **disabled**, config/rule validation before restart (promtool), size cap, WAL
  recovery automatic, no remote write.
- **Tempo:** internal-only, filesystem perms, retention enforced, WAL/compactor
  recovery, object-storage migration criteria documented, no multi-tenancy, no
  sensitive trace tags.
- **Collector:** TLS receiver, bearer auth, memory_limiter + batch + bounded
  retry queue, health endpoint, debug exporter disabled (verbosity none),
  read-only secret mounts, graceful shutdown, config validation. Verified it
  **refuses to start** when cert/key/auth secret missing or config invalid
  (concise errors, no secret leakage).

## 20. Failure-isolation results

Repeated/expanded outage tests (live, on disposable + active stacks):
- Collector down → Service remains operational (OTLP fails in isolation).
- Prometheus down → Collector still exports traces to Tempo.
- Tempo down → Collector still exposes metrics to Prometheus.
- Grafana down → ingestion healthy.
- Alertmanager down → Prometheus keeps evaluating rules.
- node-exporter down → application telemetry unaffected.
- Invalid TLS/auth → export fails safely; Service keeps running.
- Storage-full simulation (disposable, small volume) → components fail
  predictably; Service unaffected.

## 21. Application build/test results (Phase 33.5)

No application telemetry/business code changed. `dotnet build` + `dotnet test`
full suite green; Observability 272/272; Service 44/44; Stress 12/12;
Benchmarks Release 0 errors. Only appsettings *example* file added
(`appsettings.Observability.Production.example.json`), not loaded automatically.

## 22. Known gaps / non-goals

- No Kubernetes, Loki/Alloy, Jaeger/Zipkin, Elasticsearch, ClickHouse, cAdvisor,
  or bundled reverse proxy.
- Tempo stays filesystem-backed (object storage documented as scale trigger).
- Continuous Prometheus/Tempo backup not performed (disposable telemetry).
- mTLS optional, not enabled.
- `latest` tags never used.

## 23. Phase 33.6 next scope

Suggested: object-storage backend for Tempo at scale; a rendered-config
deployment pipeline (envsubst/secret management) as code; SLO/error-budget
alerting on top of the recording rules; and a runbook for the production overlay
itself (bring-up, certificate renewal, secret rotation).
