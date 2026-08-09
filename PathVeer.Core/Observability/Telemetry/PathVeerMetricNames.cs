namespace PathVeer.Core.Observability.Telemetry;

/// <summary>
/// Exact, constant metric names approved by the Phase 32.1 observability
/// architecture. All names share the <c>pathveer.</c> prefix. Instruments
/// are intentionally NOT created in this phase; this is a names-only contract.
/// </summary>
public static class PathVeerMetricNames
{
    // Counters
    public const string RuntimeCyclesStarted = "pathveer.runtime.cycles.started";
    public const string RuntimeCyclesCompleted = "pathveer.runtime.cycles.completed";
    public const string RuntimeCyclesFailed = "pathveer.runtime.cycles.failed";
    public const string RuntimeCyclesCancelled = "pathveer.runtime.cycles.cancelled";
    public const string RuntimeRepairsWithChanges = "pathveer.runtime.repairs.with_changes";
    public const string RuntimeRepairsNoChanges = "pathveer.runtime.repairs.no_changes";
    public const string RoutesOperationsRequested = "pathveer.routes.operations.requested";
    public const string RoutesOperationsSucceeded = "pathveer.routes.operations.succeeded";
    public const string RoutesOperationsFailed = "pathveer.routes.operations.failed";
    public const string PrefixChecks = "pathveer.prefix.checks";
    public const string DnsLookups = "pathveer.dns.lookups";
    public const string IpcRequests = "pathveer.ipc.requests";
    public const string SupportBundlesExported = "pathveer.support.bundles.exported";
    public const string SupportBundlesFailed = "pathveer.support.bundles.failed";

    // Histograms
    public const string RuntimeCycleDuration = "pathveer.runtime.cycle.duration";
    public const string RuntimeObserveDuration = "pathveer.runtime.observe.duration";
    public const string RuntimePlanningDuration = "pathveer.runtime.planning.duration";
    public const string RuntimeExecutionDuration = "pathveer.runtime.execution.duration";
    public const string RoutesSystemCallDuration = "pathveer.routes.system_call.duration";
    public const string PrefixCheckDuration = "pathveer.prefix.check.duration";
    public const string DnsLookupDuration = "pathveer.dns.lookup.duration";
    public const string IpcRequestDuration = "pathveer.ipc.request.duration";
    public const string SupportBundleDuration = "pathveer.support.bundle.duration";
    public const string RuntimeOperationsPerCycle = "pathveer.runtime.operations.per_cycle";
    public const string RuntimeChangedRoutes = "pathveer.runtime.changed_routes";

    // Observable gauges
    public const string ServiceEnabled = "pathveer.service.enabled";
    public const string RuntimeWorkerActive = "pathveer.runtime.worker.active";
    public const string PrefixKnownCount = "pathveer.prefix.known_count";
    public const string RoutesInventoryCount = "pathveer.routes.inventory_count";
    public const string DnsCacheRecordCount = "pathveer.dns.cache_record_count";
    public const string PrefixConsecutiveFailures = "pathveer.prefix.consecutive_failures";
}
