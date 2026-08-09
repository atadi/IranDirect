# PathVeer observability — Production overlay (Phase 33.5)

This directory contains the **production-hardening overlay** for the
`deployment/observability` stack. The base `docker-compose.yml` stays the
local-development definition; this overlay adds TLS, authenticated OTLP,
host/storage monitoring, production retention, and production notification
routing — without editing the base.

## Layout

```
production/
  README.md                                  # this file
  .env.production.example                    # template; copy to .env.production (ignored)
  docker-compose.production.yml              # Compose override (merge on top of base)
  collector-otlp-tls.yaml                    # hardened Collector config (TLS + bearer auth)
  prometheus/
    prometheus.production.yml                # prod Prometheus config (size cap + node-exporter)
    production-rules/
      pathveer-host-alert-rules.yml        # storage/memory/inode alerts (needs node-exporter)
  alertmanager/
    alertmanager.production.example.yml      # email/webhook/PagerDuty routes, placeholders only
  certificates/
    README.md
    generate-dev-certs.ps1                   # DEV-ONLY cert generator -> generated/ (ignored)
  secrets/                                   # git-ignored; real secret files at deploy time
    README.md
```

## Bring it up

```bash
cd deployment/observability
cp .env.example .env                         # local dev values (Grafana creds)
cp production/.env.production.example .env.production
# 1. place TLS material + grafana password in secrets/ (see secrets/README.md)
#    and set OTLP_BEARER_TOKEN in .env.production (git-ignored)
# 2. render the Alertmanager config:
#      envsubst < production/alertmanager/alertmanager.production.example.yml > secrets/alertmanager.production.yml
docker compose -f docker-compose.yml -f docker-compose.production.yml up -d --wait
```

Compose merges the two files: services in the overlay override the base
(`command`, `volumes`, `environment`, `ports`, `restart`, `deploy.resources`,
`secrets`), and `node-exporter` is added as the sixth service.

## What the overlay changes vs local

| Aspect | Local (base) | Production overlay |
|--------|--------------|--------------------|
| OTLP transport | plaintext gRPC/HTTP, loopback | **TLS** + **bearer-token auth** |
| Collector config | `collector/otel-collector.yaml` | `production/collector-otlp-tls.yaml` |
| Host monitoring | none | **node-exporter** (6th svc) → storage/memory/inode alerts |
| Prometheus retention | 30d time only | 30d **+ 40GB size cap** |
| Alertmanager | no-op `null` receiver | email/webhook/PagerDuty (placeholder example) |
| Grafana | http, anon off | **https**, secure cookies, password from file |
| Resources | guidance only | **bounded `deploy.resources`** |
| Restart | unless-stopped | **always** |

## Secret model

- Real secret files live in `secrets/` (git-ignored). Configs reference only
  **paths**, never values.
- The Collector reads the bearer token from the `OTLP_BEARER_TOKEN` environment
  variable (set in `.env.production`, git-ignored) via `${env:OTLP_BEARER_TOKEN}`;
  TLS cert/key via `cert_file`/`key_file` paths. None appear in YAML.
- The Alertmanager production config is rendered with `envsubst` from
  `.env.production` into `secrets/alertmanager.production.yml` (git-ignored).
  Alertmanager does **not** expand `${VAR}` itself, so rendering at deploy is
  required; there is no fragile in-container templating entrypoint.

## TLS

- One-way TLS (Collector server cert) + bounded bearer token. mTLS is documented
  as an option in `certificates/README.md` but not enabled by default.
- Dev certs: `certificates/generate-dev-certs.ps1` → `certificates/generated/`
  (ignored). Self-signed, untrusted — local handshake testing only.
- The Service trusts the Collector's CA (Windows trust store, or available to
  the Service's TLS validation). Service config example:
  `PathVeer.Service/appsettings.Observability.Production.example.json`.

## Host monitoring scope

node-exporter is internal-only, mounts host `/proc` `/sys` `/` read-only, and
excludes pseudo/filesystem mounts. The host/storage alerts target **only the
host root filesystem** (where the named Docker volumes live) — not every mount.

## Failure isolation

The overlay preserves the decoupled architecture: Service → Collector (TLS);
Collector → Tempo/Prometheus; Prometheus → Alertmanager. Stopping any component
does not cascade (verified in the phase-33.5 live test). Invalid TLS/auth causes
the Service's OTLP export to fail in isolation — the Service keeps running.

## Known gaps / non-goals

- No Kubernetes, no Loki/Alloy, no additional exporters beyond node-exporter,
  no reverse proxy is bundled (firewall/TLS-termination ownership is the
  deployer's — see the phase-33.5 doc). `latest` tags are never used.
- Tempo remains filesystem-backed; object-storage migration is documented as a
  scale trigger, not implemented here.
- Prometheus/Tempo continuous backup is intentionally **not** performed; config
  is reproducible and telemetry is disposable (see `../scripts`).
