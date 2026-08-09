# Phase 36.6 — PathVeer Telemetry & Observability Identity Migration

Branch: `development/service-authority`
Base: `bf99185` (Phase 36.5A — CLI/Tray rebrand)
Scope: telemetry identity + observability deployment/docs only. Routing,
planner/executor, journal schema, route ownership, country semantics, state-root
behavior, named-pipe wire protocol, service installation and installer/upgrader
flow are untouched.

---

## 1. Migration contract — single hard cutover

Phase 36.1 deferred telemetry to avoid splitting Prometheus/Tempo history early.
Phase 36.6 resolves that deferral with a **hard cutover, no dual publication**:

- New PathVeer builds emit **only** `PathVeer.Core` (ActivitySource + Meter),
  `service.name = PathVeer.Service`, and `pathveer.*` instruments.
- Old `irandirect_*` series and `IranDirect.Service` traces remain in storage as
  **historical data only**. They are never re-emitted.
- Dual emission was rejected: it would double series and cardinality, force
  every alert/recording rule to carry an `or` branch, and create a removal
  problem later with no operational benefit at a v1 rebrand boundary.

**Cutover boundary** = the first start of a PathVeer build with observability
enabled. Before that timestamp, query `irandirect_*`; after it, `pathveer_*`.
There is no continuous single-series history across the boundary and the
dashboards do not pretend otherwise.

---

## 2. Telemetry identity

| Concern | Before | After |
|---|---|---|
| ActivitySource name | `IranDirect.Core` | `PathVeer.Core` |
| Meter name | `IranDirect.Core` | `PathVeer.Core` |
| OTel resource `service.name` | `IranDirect.Service` | `PathVeer.Service` |
| Metric instrument prefix | `irandirect.` | `pathveer.` |
| Prometheus-normalized series | `irandirect_*` | `pathveer_*` |
| Recording-rule prefix | `irandirect:` | `pathveer:` |

`service.version` (Core assembly version) and `deployment.environment.name` are
unchanged. No resource attribute was added — `service.instance.id`, `host.name`,
`user.name`, machine name, IP and filesystem paths remain absent.

---

## 3. Source-level type rename

| Before | After |
|---|---|
| `IranDirectTelemetry` | `PathVeerTelemetry` |
| `IranDirectMetricNames` | `PathVeerMetricNames` |
| `IranDirectActivityNames` | `PathVeerActivityNames` |
| `IranDirectTagNames` | `PathVeerTagNames` |
| `IranDirectTagValues` | `PathVeerTagValues` |
| `AddIranDirectObservability(...)` | `AddPathVeerObservability(...)` |

Files moved with `git mv` (rename detected by git, history preserved):

```
PathVeer.Core/Observability/Telemetry/IranDirectTelemetry.cs      -> PathVeerTelemetry.cs
PathVeer.Core/Observability/Telemetry/IranDirectMetricNames.cs    -> PathVeerMetricNames.cs
PathVeer.Core/Observability/Telemetry/IranDirectActivityNames.cs  -> PathVeerActivityNames.cs
PathVeer.Core/Observability/Telemetry/IranDirectTagNames.cs       -> PathVeerTagNames.cs
PathVeer.Core/Observability/Telemetry/IranDirectTagValues.cs      -> PathVeerTagValues.cs
PathVeer.Core.Tests/.../IranDirectTelemetryTests.cs               -> PathVeerTelemetryTests.cs
```

No misleading legacy type name is retained. There is no compatibility shim or
type alias — the cutover is complete at the symbol level.

---

## 4. Complete metric rename map

Every instrument keeps its **unit, instrument type, description, tag set and
recording semantics**. Only the brand segment of the name changed.

### Counters
| Before | After |
|---|---|
| `irandirect.runtime.cycles.started` | `pathveer.runtime.cycles.started` |
| `irandirect.runtime.cycles.completed` | `pathveer.runtime.cycles.completed` |
| `irandirect.runtime.cycles.failed` | `pathveer.runtime.cycles.failed` |
| `irandirect.runtime.cycles.cancelled` | `pathveer.runtime.cycles.cancelled` |
| `irandirect.runtime.repairs.with_changes` | `pathveer.runtime.repairs.with_changes` |
| `irandirect.runtime.repairs.no_changes` | `pathveer.runtime.repairs.no_changes` |
| `irandirect.routes.operations.requested` | `pathveer.routes.operations.requested` |
| `irandirect.routes.operations.succeeded` | `pathveer.routes.operations.succeeded` |
| `irandirect.routes.operations.failed` | `pathveer.routes.operations.failed` |
| `irandirect.prefix.checks` | `pathveer.prefix.checks` |
| `irandirect.dns.lookups` | `pathveer.dns.lookups` |
| `irandirect.ipc.requests` | `pathveer.ipc.requests` |
| `irandirect.support.bundles.exported` | `pathveer.support.bundles.exported` |
| `irandirect.support.bundles.failed` | `pathveer.support.bundles.failed` |

### Histograms
| Before | After |
|---|---|
| `irandirect.runtime.cycle.duration` | `pathveer.runtime.cycle.duration` |
| `irandirect.runtime.observe.duration` | `pathveer.runtime.observe.duration` |
| `irandirect.runtime.planning.duration` | `pathveer.runtime.planning.duration` |
| `irandirect.runtime.execution.duration` | `pathveer.runtime.execution.duration` |
| `irandirect.routes.system_call.duration` | `pathveer.routes.system_call.duration` |
| `irandirect.prefix.check.duration` | `pathveer.prefix.check.duration` |
| `irandirect.dns.lookup.duration` | `pathveer.dns.lookup.duration` |
| `irandirect.ipc.request.duration` | `pathveer.ipc.request.duration` |
| `irandirect.support.bundle.duration` | `pathveer.support.bundle.duration` |
| `irandirect.runtime.operations.per_cycle` | `pathveer.runtime.operations.per_cycle` |
| `irandirect.runtime.changed_routes` | `pathveer.runtime.changed_routes` |

### Observable gauges (reserved names, not yet instrumented)
| Before | After |
|---|---|
| `irandirect.service.enabled` | `pathveer.service.enabled` |
| `irandirect.runtime.worker.active` | `pathveer.runtime.worker.active` |
| `irandirect.prefix.known_count` | `pathveer.prefix.known_count` |
| `irandirect.routes.inventory_count` | `pathveer.routes.inventory_count` |
| `irandirect.dns.cache_record_count` | `pathveer.dns.cache_record_count` |
| `irandirect.prefix.consecutive_failures` | `pathveer.prefix.consecutive_failures` |

31 instrument names total, all migrated. No instrument was added or removed.

---

## 5. Span-name audit

Only the five **brand-derived root spans** were renamed:

| Before | After |
|---|---|
| `IranDirect.RuntimeCycle` | `PathVeer.RuntimeCycle` |
| `IranDirect.PrefixUpdateCheck` | `PathVeer.PrefixUpdateCheck` |
| `IranDirect.CustomRouteRefresh` | `PathVeer.CustomRouteRefresh` |
| `IranDirect.IpcRequest` | `PathVeer.IpcRequest` |
| `IranDirect.SupportBundleExport` | `PathVeer.SupportBundleExport` |

All 24 brand-neutral semantic child spans are **unchanged**: `Runtime.Observe`,
`Runtime.BuildDecision`, `Runtime.BuildPreview`, `Runtime.PlanChanges`,
`Runtime.Execute`, `Runtime.PersistInventory`, `Routes.Enumerate`,
`Routes.Create`, `Routes.Delete`, `Prefix.HttpHead`, `Prefix.HttpGet`,
`Prefix.Compare`, `Prefix.PersistMetadata`, `Dns.CacheRead`, `Dns.Resolve`,
`Dns.CacheWrite`, `Ipc.Connect`, `Ipc.Send`, `Ipc.Dispatch`, `Ipc.Receive`,
`Support.CaptureSnapshot`, `Support.Serialize`, `Support.WriteJson`,
`Support.CreateZip`. No semantic span was churned for cosmetic consistency.

---

## 6. Tag contract — unchanged

No tag key or tag value was renamed. The approved bounded keys (`operation`,
`outcome`, `trigger`, `route_kind`, `change_kind`, `source`, `cache_state`,
`ipc_command`, `diagnostic_severity`, `service_state`, `failure_category`) carry
no branding, so the rename does not touch them. The full prohibited list
(`destination_prefix`, `gateway`, `next_hop`, `interface_index`,
`interface_name`, `domain`, `dns_domain`, `output_path`, `file_path`,
`pipe_payload`, `machine_name`, `user_name`, `exception_message`, `url`,
`endpoint`, `route_identity`, `diagnostic_id`, `execution_step_identity`) is
intact. No dimension was added; cardinality is byte-for-byte the same shape.

---

## 7. OTel registration (Service)

`PathVeer.Service/Observability/ObservabilityServiceCollectionExtensions.cs`:
`AddIranDirectObservability` → `AddPathVeerObservability`; tracing subscribes via
`.AddSource(PathVeerTelemetry.SourceName)` and metrics via
`.AddMeter(PathVeerTelemetry.SourceName)` — a single `PathVeer.Core`
subscription each. No `IranDirect.Core` subscription remains anywhere.
`Program.cs` calls the renamed extension.

`ObservabilityOptions.ServiceName` default and all four appsettings files
(`appsettings.json`, `appsettings.Development.json`,
`appsettings.Observability.Local.example.json`,
`appsettings.Observability.Production.example.json`) now carry
`"ServiceName": "PathVeer.Service"`. `Enabled` defaults, sampling defaults,
timeouts, OTLP endpoint semantics and auth semantics are unchanged.

---

## 8. Deployment layer

### Renamed files (`git mv`, mounts/provisioning updated atomically)
```
prometheus/rules/irandirect-recording-rules.yml -> pathveer-recording-rules.yml
prometheus/rules/irandirect-alert-rules.yml     -> pathveer-alert-rules.yml
production/prometheus/production-rules/irandirect-host-alert-rules.yml
                                                -> pathveer-host-alert-rules.yml
grafana/dashboards/irandirect-ipc-support.json            -> pathveer-ipc-support.json
grafana/dashboards/irandirect-prefix-dns.json             -> pathveer-prefix-dns.json
grafana/dashboards/irandirect-reliability-errors.json     -> pathveer-reliability-errors.json
grafana/dashboards/irandirect-runtime-reconciliation.json -> pathveer-runtime-reconciliation.json
grafana/dashboards/irandirect-service-overview.json       -> pathveer-service-overview.json
```
Prometheus mounts the rules directory by glob, so no mount path changed.

### Recording rules
Group `irandirect_recording` → `pathveer_recording`. All 20 rules renamed
`irandirect:<x>` → `pathveer:<x>` with source queries repointed to `pathveer_*`.
Math, windows, guards (`> 0` denominators), quantiles, labels and units are
byte-identical apart from the identifier.

### Alert rules
Group `irandirect_alerts` → `pathveer_alerts` (15 rules); host group
`irandirect_host_alerts` → `pathveer_host_alerts` (4 rules). Every alert name
rebranded, e.g. `IranDirectRuntimeCycleFailureRateHigh` →
`PathVeerRuntimeCycleFailureRateHigh`, `IranDirectCollectorUnavailable` →
`PathVeerCollectorUnavailable`, `IranDirectPrometheusStoragePressure` →
`PathVeerPrometheusStoragePressure`. Thresholds, `for` durations, severity
labels, grouping, inhibition logic and runbook annotations are unchanged. No
alert was added, removed or retuned.

### Grafana
Folder `IranDirect` → `PathVeer`; provisioning provider name likewise. Titles
`IranDirect / …` → `PathVeer / …` across all five dashboards. **Dashboard UIDs
renamed** `irandirect-*` → `pathveer-*`: this is the product rebrand boundary
and the cleanest point to take the break. Consequence, documented deliberately:
**existing dashboard URLs and browser bookmarks will 404 and must be
re-bookmarked.** All PromQL repointed to `pathveer_*` / `pathveer:*`; the Tempo
explore link now filters `service.name = PathVeer.Service`. No panel was
redesigned, added or removed (19/14/22/22/20 panels, unchanged).

### Grafana datasource UIDs
`irandirect-prometheus` → `pathveer-prometheus`, `irandirect-tempo` →
`pathveer-tempo`, renamed atomically with every dashboard reference and the
trace-to-metrics `datasourceUid` links. Generic component names (`prometheus`,
`tempo`, `grafana`) were left alone — they carry no branding.

### Tempo
Search/query references now use `service.name = PathVeer.Service`. Stored
historical traces are **not** rewritten and remain searchable under
`IranDirect.Service`. Tempo's own config carried only a comment reference.

### Collector
Only branded references changed: the resource processor's `service.name` value
is now `PathVeer.Service`. **No** transform processor, attribute rewriting,
tail sampling, queue/retry change, or TLS/auth change was introduced. Phase 33
hardening is fully intact.

### Alertmanager
Inhibition source alert names updated (`PathVeerCollectorUnavailable`,
`PathVeerPrometheusTargetDown`) and the notification template header rebranded.
Receivers, routing policy, secret handling, inhibition semantics and
notification strategy are unchanged; no integration was added.

### Compose / production overlay
Project name and network `irandirect-observability` → `pathveer-observability`;
container names `irandirect-*` → `pathveer-*` (collector, prometheus, tempo,
alertmanager, grafana, node-exporter). `docker-compose.production.yml` header,
production Prometheus `monitor` external label (`pathveer-production`), the
production collector TLS config and the Alertmanager production example were
rebranded. TLS, auth and retention behavior are unchanged. The dev-CA CN is now
`pathveer-dev-ca`.

### Docker volumes — DECISION: keep legacy names (option A)
`irandirect-prometheus-data`, `irandirect-tempo-data`, `irandirect-grafana-data`,
`irandirect-alertmanager-data`, `irandirect-node-exporter-data` **keep their
legacy names**. Renaming a Docker named volume does not migrate data: it creates
a new empty volume and orphans the existing TSDB, Tempo blocks, Grafana SQLite
DB and Alertmanager state. For opaque infrastructure identifiers, data
continuity beats brand consistency. The decision is recorded as an inline
comment in both compose files and in `deployment/observability/README.md §5`.
Container/project/network names *were* rebranded because they hold no state.
No volume was deleted; `docker compose down -v` was never run.

### Backup / restore
`backup.ps1`, `restore.ps1`, `verify-backup.ps1`: archive/stack naming
`irandirect-obs-*` → `pathveer-obs-*`. The scripts capture the rules and
dashboards directories wholesale (by directory, not by filename), so the renamed
rule/dashboard files are captured automatically. Secret exclusions, hash
verification and restore semantics are unchanged.

---

## 9. Documentation & runbooks

Updated as **current operational instructions**:
`docs/observability/README.md`, `docs/observability/operations.md`,
`deployment/observability/README.md`, `deployment/observability/production/README.md`,
`deployment/observability/production/certificates/README.md`, and all 17
runbooks under `deployment/observability/runbooks/` (alert names, metric names,
dashboard names, datasource UIDs, service references).

Historical Phase 32/33 phase documents were intentionally **not** rewritten —
they record what was true at the time. Legacy path references such as
`%ProgramData%\IranDirect` remain wherever they describe legacy state.

---

## 10. Tests

Updated to assert the new identity: `TelemetryArchitectureTests`,
`TelemetryNameCatalogTests`, `TelemetryEnumMappingTests`,
`TelemetryTagCatalogTests`, `TelemetryAllocationTests`, the per-workflow
telemetry test classes, `PathVeerTelemetryTests` (renamed), `BrandingTests`
(`TelemetryIdentityFrozen` → `TelemetryIdentityIsPathVeer`),
`FaultInjectionArchitectureTests` (stale project-folder paths), and the four
Service observability test classes.

New: `PathVeer.Core.Tests/Observability/Telemetry/TelemetryBrandingMigrationTests.cs`
— 9 tests proving source/meter are exactly `PathVeer.Core`, no legacy identity
remains, exactly one ActivitySource and one Meter are defined, every metric name
uses the `pathveer.` prefix, none uses `irandirect.`, names are unique (no dual
publication), branded spans migrated while neutral spans are untouched, no span
carries legacy branding, and the tag/privacy contract is unchanged.

---

## 11. Verification (fresh, this session)

| Gate | Result |
|---|---|
| `dotnet clean` + `dotnet build PathVeer.slnx -c Debug` | 0 errors, 104 warnings (baseline 104, unchanged) |
| `PathVeer.Core.Tests` | 2428 passed, 0 failed |
| `PathVeer.Service.Tests` | 50 passed, 0 failed |
| Stress (`Category=Stress`, inside Core.Tests) | 12 passed, 0 failed |
| `PathVeer.Benchmarks` Release build | 0 errors, 2 warnings |
| Focused `Observability` | 281 passed |
| Focused `Telemetry` | 237 passed |
| Focused `Branding` | 20 passed |
| Focused `Ipc` | 123 passed |
| Focused `StateRoot` | 26 passed |
| Focused `Country` | 170 passed |
| Focused `Globalization` | 120 passed |
| Focused Service `Observability` | 36 passed |
| `promtool check rules` (recording) | SUCCESS — 20 rules (was 20) |
| `promtool check rules` (alerts) | SUCCESS — 15 rules (was 15) |
| `promtool check rules` (host alerts) | SUCCESS — 4 rules (was 4) |
| `promtool check config` | SUCCESS |
| Dashboard JSON validation | 5 dashboards, unique UIDs, unique panel IDs, `editable:false`, PathVeer folder/titles, 0 `irandirect` occurrences |
| Live Prometheus rule load (throwaway container, port 19090) | groups `pathveer_recording` (20 rules, health `ok`) + `pathveer_alerts` (15 rules) loaded from the renamed files |
| Observability data continuity | `irandirect-{prometheus,tempo,grafana,alertmanager}-data` volumes present and untouched; user's running stack left up; `docker compose down -v` never run |

Live-stack limitation, stated honestly: the user's long-running observability
stack has its config bind-mounted from a pre-rename path (`C:\codespace\irandirect\…`,
which no longer exists on disk), so it cannot hot-reload this branch's config
and still serves the old `irandirect_recording`/`irandirect_alerts` groups. Rule
loading was therefore validated in a **separate throwaway Prometheus container**
mounting this branch's files (removed afterwards); the user's stack and all four
data volumes were left exactly as found. End-to-end emission of live
`pathveer_*` series from a running PathVeer.Service against a rebranded stack is
deferred to whenever the user next brings the stack up from this branch — the
Prometheus TSDB currently contains no `irandirect_*` series at all (805 metric
names, none branded), so there is no historical application-telemetry series to
lose in this environment.

Observability-disabled behavior (Phase 32.9 guarantee) and collector-unavailable
failure isolation are covered by the existing Service observability tests
(36 passed), which pass unchanged apart from the renamed symbols.

---

## 12. Remaining IranDirect identifiers (all explicitly legacy)

1. `IranDirect.Control.v1` — legacy compatibility pipe (`PathVeerPipeNames.LegacyPipeName`)
2. `%ProgramData%\IranDirect` — legacy state root (`StateRootResolver.LegacyStateDirectoryName`)
3. `LegacyServiceNames.ServiceName = "IranDirect"` — legacy service migration input
4. `iran-ipv4-prefixes.txt` — legacy prefix filename
5. `irandirect-*-data` Docker volumes — deliberate data-continuity decision (§8)
6. Historical docs (Phase 32/33/34/35/36.1–36.5), fixtures and migration tests
7. `AddedByIranDirect` — persisted route-inventory field (schema, out of scope)
8. Domain prose in non-observability source (e.g. route-ownership messages) —
   owned by a later slice, not telemetry identity

**Zero** active telemetry identity, metric prefix, dashboard title, Prometheus
query, alert name, OTel `service.name`, or current observability doc branding
uses IranDirect.

---

## 13. Phase 36.7 boundary

36.6 stops at the observability layer. Phase 36.7 owns the installer/upgrader,
old `IranDirect` service cleanup, compatibility launchers/aliases,
shortcut/startup migration, installed artifact layout, upgrade orchestration and
rollback tooling. No installer file was touched in this phase.
