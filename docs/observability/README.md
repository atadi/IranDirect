# Observability

Telemetry architecture and instrumentation plan for IranDirect.

- [Phase 32.1 observability architecture and telemetry baseline](phase-32.1-observability-architecture.md)
  — mechanism inventory, workflow maps, ActivitySource/metrics contracts,
  bounded tags, privacy matrix, RuntimePerfReport decision, phased roadmap.
- [Phase 32.2 core telemetry contracts](phase-32.2-core-telemetry-contracts.md)
  — `ActivitySource`/`Meter` definitions, span/metric/tag catalogs, bounded
  values, enum + failure-category mappers, tag validator, 90 contract tests.

Status: contracts implemented; workflows not yet instrumented. Implementation
continues at Phase 32.3.
