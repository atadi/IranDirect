# Observability

Telemetry architecture and instrumentation plan for IranDirect.

- [Phase 32.1 observability architecture and telemetry baseline](phase-32.1-observability-architecture.md)
  — mechanism inventory, workflow maps, ActivitySource/metrics contracts,
  bounded tags, privacy matrix, RuntimePerfReport decision, phased roadmap.
- [Phase 32.2 core telemetry contracts](phase-32.2-core-telemetry-contracts.md)
  — `ActivitySource`/`Meter` definitions, span/metric/tag catalogs, bounded
  values, enum + failure-category mappers, tag validator, 90 contract tests.
- [Phase 32.3 runtime cycle tracing and metrics](phase-32.3-runtime-cycle-telemetry.md)
  — instruments the top-level runtime reconciliation cycle only: one root
  `IranDirect.RuntimeCycle` activity, four counters, one duration histogram,
  bounded outcome/trigger/failure-category tags; controller boundary, no child
  spans, no OTel packages, profiler-independent.
- [Phase 32.4 runtime planning telemetry](phase-32.4-runtime-planning-telemetry.md)
  — instruments change-set planning as a child `Runtime.PlanChanges` activity
  under the runtime-cycle root; planning-duration + changed-routes histograms;
  bounded outcome/failure-category; reconciler owner, no planner lifecycle
  counters, no `change_bucket` (not approved).
- [Phase 32.5 runtime execution telemetry](phase-32.5-runtime-execution-telemetry.md)
  — instruments runtime execution as a child `Runtime.Execute` activity under the
  runtime-cycle root; execution-duration + operations-per-cycle histograms; bounded
  outcome/failure-category; controller orchestration owner, `RuntimeExecutor`
  stays telemetry-free, no per-step or per-route spans.
- [Phase 32.6 route system-call telemetry](phase-32.6-route-system-call-telemetry.md)
  — instruments the native route boundary (`WindowsRouteApi` via the
  `TelemetryRouteApi` decorator) with `Routes.Enumerate` / `Routes.Create` /
  `Routes.Delete` child activities under `Runtime.Execute`; three route-operation
  counters + one system-call duration histogram; bounded operation/change-kind/
  route-kind tags; no per-route spans or metrics, no OpenTelemetry packages.
- [Phase 32.7 prefix update and DNS telemetry](phase-32.7-prefix-and-dns-telemetry.md)
  — instruments the official prefix update check (`IranDirect.PrefixUpdateCheck`,
  HEAD→optional GET→optional Compare) and the custom-route DNS refresh
  (`IranDirect.CustomRouteRefresh`, cache-read→resolve→cache-write); approved
  prefix + DNS counters and duration histograms; bounded operation/source/outcome/
  cache-state/trigger/failure-category tags; no per-prefix or per-address spans,
  no URLs/domains/IPs attached, no OpenTelemetry packages.
- [Phase 32.8 IPC and support export telemetry](phase-32.8-ipc-and-support-export-telemetry.md)
  — instruments the named-pipe IPC request/response (`IranDirect.IpcRequest` root
  + `Ipc.Connect`/`Ipc.Send`/`Ipc.Receive` children, plus an independent
  `Ipc.Dispatch` server root) and the support snapshot/bundle export
  (`IranDirect.SupportBundleExport` shared root + `Support.CaptureSnapshot`/
  `Support.Serialize`/`Support.WriteJson`/`Support.CreateZip` children); IPC and
  support counters + duration histograms; bounded operation/ipc_command/outcome/
  failure-category tags; no duplicate roots when a bundle drives the snapshot
  exporter, no payload/path/identity attachments, no OpenTelemetry packages.
- [Phase 32.9 OpenTelemetry hosting and export configuration](phase-32.9-opentelemetry-hosting.md)
  — adds **optional**, disabled-by-default OpenTelemetry hosting inside
  `IranDirect.Service`: tracing/metrics registration for the existing Core
  ActivitySource/Meter, optional OTLP and Development-only console export, bounded
  resource attributes, options validation, failure/shutdown isolation. No new
  workflow instrumentation; Core contracts unchanged; Core remains BCL-only.
- [Phase 33.1 telemetry consumption architecture](phase-33.1-telemetry-consumption-architecture.md)
  — analysis and deployment design for consuming the emitted telemetry:
  committed telemetry inventory, deployment assumptions, candidate stack
  comparison (Collector+Prometheus+Tempo+Grafana selected), Collector design,
  metrics/trace storage, sampling, dashboard catalog, recording rules, alert
  catalog, notification routing, privacy/security, retention/sizing,
  backup/recovery, deployment topology, environment separation, runbook
  requirements, failure-isolation confirmation, and a phased roadmap
  (33.2–33.6). Documentation only — no infrastructure, no source changes.
- [Phase 33.2 local observability stack](../../deployment/observability/README.md)
  — reproducible local Docker Compose stack implementing the Phase 33.1 design:
  OpenTelemetry Collector (sole OTLP endpoint, gRPC 4317 / HTTP 4318),
  Prometheus (15s scrape, 30-day retention), Tempo (filesystem backend, local
  blocks) and Grafana (Prometheus + Tempo datasources provisioned, anonymous
  access disabled, admin credentials via environment only). Named volumes,
  health checks, loopback-only host ports, `.env.example` placeholders, and
  documented startup/shutdown/wipe/manual-verification procedures. Consumer
  only — no application telemetry changes. Lives in
  [`deployment/observability/`](../../deployment/observability/).
- [Phase 33.3 dashboards and recording rules](phase-33.3-dashboards-and-recording-rules.md)
  — adds version-controlled Grafana dashboards (five, in the `IranDirect`
  folder) and a Prometheus recording-rule group (`irandirect_recording`) for the
  telemetry the stack already receives. Documents the metric-name→Prometheus
  translation, label inventory, which contract metrics are not yet emitted, the
  validation results, and the trace-link limitation. No application changes
  beyond a collector pipeline-output toggle.
- [Phase 33.4 alerts and runbooks](phase-33.4-alerts-and-runbooks.md)
  — adds Alertmanager as the fifth stack service, fifteen Prometheus alert rules
  (`irandirect_alerts`), a no-op default receiver, three inhibition rules, a
  severity/grouping/routing model, one runbook per alert under
  `deployment/observability/runbooks/`, and dashboard alert/runbook links.
  Documents the alert inventory, thresholds, known coverage gaps, and the
  Phase 33.5 next scope. Infrastructure/documentation only — no application
  telemetry changes.
- [Phase 33.5 production hardening](phase-33.5-production-hardening.md)
  — adds a production Compose overlay (`docker-compose.production.yml` +
  `production/`) hardening the stack for production-like deployment: TLS OTLP
  with bearer-token auth, Docker-secret delivery, Alertmanager production
  notification routing (email/webhook/PagerDuty placeholders), node-exporter
  for host/storage alerts (closing the Phase 33.4 storage-pressure gap),
  Prometheus size-based retention cap, backup/restore scripts, dev certificate
  generation, upgrade/rollback, reverse-proxy/firewall model, and per-component
  hardening. No application telemetry changes; no committed secrets.
- [Observability operations guide](operations.md)
  — how to enable/configure OTLP and console export, environment-variable secret
  handling, sampling, collector-unavailable behavior, shutdown/flush, privacy
  guarantees, and troubleshooting.

Status: contracts implemented; runtime cycle, planning, execution, the native
route boundary, the prefix/DNS network workflows, IPC, and support export
instrumented. Optional OpenTelemetry export hosting is available (off by
default). Consumption-platform architecture is designed (Phase 33.1) and the
consumption stack (Collector + Prometheus + Tempo + Grafana) is available
under `deployment/observability/` (Phase 33.2), and Phase 33.3 adds
version-controlled dashboards and recording rules to that stack; Phase 33.4 adds
Alertmanager, fifteen alert rules, and operational runbooks; and Phase 33.5 adds
a production-hardening overlay (TLS, auth, secrets, node-exporter storage
alerts, retention caps, backup/restore). Alerts and hardening continue in
Phases 33.6+.
