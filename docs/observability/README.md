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

Status: contracts implemented; runtime cycle, planning, execution, the native
route boundary, and the prefix/DNS network workflows instrumented.
