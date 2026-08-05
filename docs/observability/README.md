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

Status: contracts implemented; runtime cycle, planning, execution, and the native
route boundary instrumented. Implementation continues with the remaining
runtime-cycle child operations.
