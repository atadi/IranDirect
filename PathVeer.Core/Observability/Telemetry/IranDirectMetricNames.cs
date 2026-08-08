namespace PathVeer.Core.Observability.Telemetry;

/// <summary>
/// Exact, constant metric names approved by the Phase 32.1 observability
/// architecture. All names share the <c>irandirect.</c> prefix. Instruments
/// are intentionally NOT created in this phase; this is a names-only contract.
/// </summary>
public static class IranDirectMetricNames
{
    // Counters
    public const string RuntimeCyclesStarted = "irandirect.runtime.cycles.started";
    public const string RuntimeCyclesCompleted = "irandirect.runtime.cycles.completed";
    public const string RuntimeCyclesFailed = "irandirect.runtime.cycles.failed";
    public const string RuntimeCyclesCancelled = "irandirect.runtime.cycles.cancelled";
    public const string RuntimeRepairsWithChanges = "irandirect.runtime.repairs.with_changes";
    public const string RuntimeRepairsNoChanges = "irandirect.runtime.repairs.no_changes";
    public const string RoutesOperationsRequested = "irandirect.routes.operations.requested";
    public const string RoutesOperationsSucceeded = "irandirect.routes.operations.succeeded";
    public const string RoutesOperationsFailed = "irandirect.routes.operations.failed";
    public const string PrefixChecks = "irandirect.prefix.checks";
    public const string DnsLookups = "irandirect.dns.lookups";
    public const string IpcRequests = "irandirect.ipc.requests";
    public const string SupportBundlesExported = "irandirect.support.bundles.exported";
    public const string SupportBundlesFailed = "irandirect.support.bundles.failed";

    // Histograms
    public const string RuntimeCycleDuration = "irandirect.runtime.cycle.duration";
    public const string RuntimeObserveDuration = "irandirect.runtime.observe.duration";
    public const string RuntimePlanningDuration = "irandirect.runtime.planning.duration";
    public const string RuntimeExecutionDuration = "irandirect.runtime.execution.duration";
    public const string RoutesSystemCallDuration = "irandirect.routes.system_call.duration";
    public const string PrefixCheckDuration = "irandirect.prefix.check.duration";
    public const string DnsLookupDuration = "irandirect.dns.lookup.duration";
    public const string IpcRequestDuration = "irandirect.ipc.request.duration";
    public const string SupportBundleDuration = "irandirect.support.bundle.duration";
    public const string RuntimeOperationsPerCycle = "irandirect.runtime.operations.per_cycle";
    public const string RuntimeChangedRoutes = "irandirect.runtime.changed_routes";

    // Observable gauges
    public const string ServiceEnabled = "irandirect.service.enabled";
    public const string RuntimeWorkerActive = "irandirect.runtime.worker.active";
    public const string PrefixKnownCount = "irandirect.prefix.known_count";
    public const string RoutesInventoryCount = "irandirect.routes.inventory_count";
    public const string DnsCacheRecordCount = "irandirect.dns.cache_record_count";
    public const string PrefixConsecutiveFailures = "irandirect.prefix.consecutive_failures";
}
