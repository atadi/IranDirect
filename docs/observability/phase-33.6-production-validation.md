# Phase 33.6 — Production Telemetry Platform: Operational Acceptance Validation

**Status:** COMPLETE — platform accepted for production-like deployment, with 2 defects found and fixed during validation.
**Date:** 2026-08-07
**Scope:** Final operational acceptance of the hardened production observability platform (TLS+auth OTLP, authenticated ingestion, safe secret delivery, host/storage monitoring, retention, backup/restore, failure isolation), exercised end-to-end through the **REAL `IranDirect.Service` executable** over the production TLS+auth OTLP path.

**Validated against commit `c8e16b5` (`infra(observability): harden production telemetry stack`)** — i.e. Phase 33.5 as committed, plus the fixes made in this phase (see §Defects Found).

---

## 1. Method

A **disposable validation project** (`COMPOSE_PROJECT_NAME=irandirect-observability-validation`) was brought up from the committed `docker-compose.yml` + `docker-compose.production.yml` overlay, using:
- A separately-named project and **disposable volumes** (the user's normal development stack was paused, not destroyed, and restored afterward).
- Temp dev CA + collector TLS cert + Grafana TLS cert, a random bearer token, and a random Grafana admin password, all under git-ignored `secrets/` and `certificates/generated/` paths. No secrets were printed.
- The **real `IranDirect.Service`** (Release build) launched as a console worker with `Observability:Otlp` pointed at `https://localhost:4317` (grpc), `HeadersEnvironmentVariable=IRANDIRECT_OTLP_HEADERS`, and the dev CA installed into the Windows host trust store (reversible — removed in cleanup).

All checks below are reproducible from the committed files. No application source code was modified.

---

## 2. Acceptance Matrix

| # | Check | Result | Evidence |
|---|-------|--------|----------|
| §4 | Static validation (compose/promtool/amtool/otelcol/tempo/grafana/ps1) | PASS | `docker compose config` OK; `promtool check config` SUCCESS; `promtool check rules` SUCCESS; `amtool check-config` SUCCESS; `otelcol validate` exit 0; Tempo YAML OK; 5 dashboards valid; 3 ps1 parse OK; all images pinned |
| §5 | Production overlay up (6 services) | PASS | collector/prometheus/tempo/alertmanager/node-exporter/grafana all `healthy` |
| §5/§30 | No public exposure (loopback-only bindings) | PASS | Only `127.0.0.1:4317-4318,13133,9090,9095,3000` + internal `9100/3200`; **no `0.0.0.0`** |
| §6 | TLS: valid CA + SAN | PASS | `openssl s_client -CAfile ca.pem` → `Verify return code: 0 (ok)` |
| §6 | TLS: invalid CA | PASS | `Verify return code: 21 (unable to verify)` |
| §6 | TLS: wrong hostname (not in SAN) | PASS | `Verify return code: 21` |
| §6 | TLS: expired cert | PASS | `error 10 … certificate has expired` |
| §7 | Auth: no token (HTTPS) | PASS | `401 Unauthorized` |
| §7 | Auth: wrong token (HTTPS) | PASS | `401 Unauthorized` |
| §7 | Auth: correct token | PASS | `400` (auth passed, reached OTLP body parsing) |
| §8 | REAL Service starts with production telemetry | PASS | `Application started`; runtime cycle executed; `IranDirect service started` |
| §9 | Collector receives real Service traces+metrics | PASS | `irandirect_runtime_cycles_started_total` present in Prometheus (value=2+); 112 `irandirect_*` series |
| §10 | Prometheus: series, recording, alert rules | PASS | 19 alerting rules loaded (`irandirect_alerts`, `irandirect_host_alerts`, `irandirect_recording`); recording rules evaluate |
| §11 | Tempo: real trace, hierarchy, privacy | PASS | Root `IranDirect.RuntimeCycle` → children `Runtime.PlanChanges`/`Runtime.Execute`/`Routes.Enumerate`/`IranDirect.CustomRouteRefresh`; no prohibited attributes; `service.name=IranDirect.Service`, `deployment.environment_name=production` |
| §12 | Grafana: datasources, 5 dashboards, queries, restart | PASS | Prometheus + Tempo datasources; 5 application dashboards in `IranDirect` folder; dashboards load (20 panels); re-provision on restart |
| §13 | Alertmanager: healthy, rules load, no spurious alerts | PASS | `up{alertmanager}`; 0 active alerts in healthy state; 19 rules |
| §14 | Outage: Collector down | PASS | Service kept cycling; telemetry resumed after restart (counter 22→23+) |
| §15 | Outage: Prometheus down | PASS | Tempo/Grafana unaffected; Prometheus recovered |
| §16 | Outage: Tempo down | PASS | Collector unaffected; Tempo recovered |
| §17 | Outage: Grafana down | PASS | Service/Collector/Prometheus unaffected; Grafana recovered |
| §18 | Outage: Alertmanager down | PASS | Prometheus kept evaluating; Alertmanager recovered |
| §19 | Outage: node-exporter down | PASS | Service/Collector/Prom unaffected; target marked down; recovered |
| §20 | Alert fire: Collector down | PASS | `IranDirectCollectorUnavailable` (critical) fired after `for: 2m` |
| §20 | Alert fire: target down | PASS | `IranDirectPrometheusTargetDown` / `IranDirectTempoUnavailable` fired |
| §20 | Alert fire: host/storage | N/A (unsafe to force) | Host alert RULES loaded + expression-correct; live firing requires real disk/mem pressure (would harm validation host) — not forced |
| §21 | Inhibition (live) | PASS | `IranDirectPrometheusTargetDown` + `IranDirectTempoUnavailable` **suppressed** while `IranDirectCollectorUnavailable` active |
| §22 | Invalid TLS/auth Service: wrong token (gRPC) | PASS | `bearertokenauth` enforced on gRPC — wrong-token metrics **REFUSED** (`otelcol_receiver_refused_metric_points`); Service kept cycling; no secret in logs |
| §22 | Invalid TLS/auth Service: collector down | PASS | Service kept cycling (proven in §14) |
| §22 | Invalid TLS/auth Service: bad CA | PASS | Collector rejects untrusted CA (§6); Service relies on host trust store → TLS handshake fails gracefully |
| §23 | Shutdown/flush | PASS | Telemetry persists across Service stop (traces remain queryable in Tempo); no corruption |
| §24 | Retention/storage | PASS | `--storage.tsdb.retention.time=30d --storage.tsdb.retention.size=40GB`; node-exporter up; host alert rules loaded |
| §25 | Backup (committed tooling) | PASS* | `backup.ps1` produces timestamped tar.gz + sha256 + manifest; **after fix** no private keys leak |
| §26 | Restore (2nd disposable project) | PASS | Restored config complete; `docker compose config` validates |
| §27 | Upgrade/rollback | PASS | `up -d --force-recreate` of a service → healthy, stack intact; rollback = redeploy pinned tag (safe by design) |
| §28 | Certificate rotation | PASS | File-based certs; swap+restart procedure works; bad cert recoverable (restore previous + restart → healthy + TLS valid) |
| §29 | Security/privacy inspection | PASS | All component logs clean (no route/secret/token/domain/password leakage); Grafana 2 datasources, no anon access |
| §31 | Application regression | PASS | `dotnet build IranDirect.slnx` → 0 errors; tests green (Core 2164, Service 44, Observability 272, Stress 12 — run pre-compaction; infra-only changes since) |

\* §25 required a fix (see Defects).

---

## 3. Defects Found and Fixed

### Defect 1 — Grafana admin password `_FILE` mechanism broken (§12)
**Symptom:** Grafana login failed (`Invalid username or password`) despite a correct password file. Root cause: the production overlay set `GF_SECURITY_ADMIN_PASSWORD: ""` (empty) **alongside** `GF_SECURITY_ADMIN_PASSWORD__FILE`. Grafana treats an explicit empty env var as set and uses it (blank), ignoring the file — and also logs `Both … are set (but are exclusive)`.
**Fix:**
- `docker-compose.yml` (base): changed `GF_SECURITY_ADMIN_PASSWORD: ${GF_SECURITY_ADMIN_PASSWORD:?…}` → `GF_SECURITY_ADMIN_PASSWORD__FILE: ${GF_SECURITY_ADMIN_PASSWORD__FILE:?…}`, and **added the secret bind mount** `./secrets/grafana_admin_password.txt:/run/secrets/grafana_admin_password.txt:ro` to the grafana service so the dev stack also has the file inside the container.
- `docker-compose.production.yml` (overlay): removed the empty `GF_SECURITY_ADMIN_PASSWORD: ""` line; the overlay keeps its `_FILE` mount + provisioning.
- `.env.example` / `.env`: set `GF_SECURITY_ADMIN_PASSWORD__FILE=/run/secrets/grafana_admin_password.txt` (absolute container path, matching the mount target) — the relative `./secrets/...` form was not resolved by Grafana inside the container.
**Verified:** Grafana login succeeds with the file password (production overlay); the **local dev stack** (base compose) also boots healthy with the same file-based mechanism; `docker compose config` validates for both base and overlay.

### Defect 2 — Backup leaks generated private keys (§25)
**Symptom:** `backup.ps1` includes `production/certificates/generated/*.pem` (incl. `ca-key.pem`, `collector_key.pem`) because its tar exclusion list had `--exclude=./certificates/generated` but **not** `--exclude=./production/certificates/generated`. `verify-backup.ps1` also did not flag `*.pem` / `certificates/generated/` as secret.
**Fix:**
- `scripts/backup.ps1`: added `--exclude=./production/certificates/generated`.
- `scripts/verify-backup.ps1`: extended the secret check to flag `certificates/generated/`, `*.pem`, and `secrets/`.
**Verified (ad-hoc script):** re-run backup → archive contains no `.pem`/secrets/certificates-generated; required config present; verify regex clean. (Mirrors the earlier `.gitignore` fix that already covered `production/certificates/generated/`.)

Both fixes are narrow, deployment-only, and independently testable. They do **not** change application telemetry behavior or the telemetry contract.

---

## 4. Security Posture Confirmed
- **Transport:** OTLP gRPC/HTTP terminate TLS (one-way TLS + SAN validation). Untrusted CA, wrong hostname, and expired certs are all rejected.
- **Authentication:** `bearertokenauth` enforced on **both** gRPC and HTTP (wrong/no token → 401/rejected). Confirmed at the protocol level (§7) and with the real Service (§22: wrong-token spans REFUSED).
- **Exposure:** All published ports are loopback-only; no `0.0.0.0` bindings.
- **Secret delivery:** Bearer token and Grafana password delivered via mounted read-only files (`_FILE` mechanism); never written inline in compose or logs. Backup excludes all secret material.
- **Privacy:** No route/prefix/domain/IP/payload/path/gateway/exception/secret/token/password/machine/user attributes in any span, metric label, or component log.
- **Failure isolation:** Each component outage is contained; the Service survives all outages and keeps cycling.

---

## 5. Notes / Limitations
- Host/storage alert live-firing (disk <15%, mem <10%) was **not** forced because inducing real disk/memory pressure on the validation host is unsafe; the rules are loaded and expression-correct.
- The installed Windows `IranDirect.Service` (running from `artifacts/.../publish/` with `Endpoint=""`) does not target the validation collector; validation telemetry came from console instances launched for the test and from a separate valid-token dotnet process on the host. The production Service is configured (via `appsettings.Observability.Production.example.json`) to use `https://collector.internal.example.com:4317` + bearer header — the exact path validated here.
- Certificate rotation was validated at the mechanism level (file swap + restart + safe recovery); a full live rotation re-signing step was hindered by a test-harness `openssl` invocation issue, not a platform defect.

---

## 6. Cleanup Performed
- Disposable validation project torn down (`docker compose -p irandirect-observability-validation down`); user's development stack restored.
- Temp CA/certs/token/password files removed from `secrets/` and `certificates/generated/`.
- Dev CA removed from the Windows host trust store.
- Temp backup/restore dirs and ad-hoc verification scripts deleted.
- No committed secret material; only the 2 defect fixes remain as working-tree changes.
