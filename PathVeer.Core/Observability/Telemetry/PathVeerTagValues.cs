namespace PathVeer.Core.Observability.Telemetry;

/// <summary>
/// Bounded, enumerated tag values for the approved telemetry tags. Every value
/// here MUST belong to a closed set; free-form values are never permitted.
/// </summary>
public static class PathVeerTagValues
{
    // outcome
    public const string Success = "success";
    public const string Failure = "failure";
    public const string Cancelled = "cancelled";
    public const string Timeout = "timeout";
    public const string NoChange = "no_change";
    public const string OutcomeUnknown = "unknown";

    // operation
    public const string OperationRuntimeCycle = "runtime_cycle";
    public const string OperationPlanChanges = "plan_changes";
    public const string OperationExecute = "execute";
    public const string OperationEnumerateRoutes = "enumerate_routes";
    public const string OperationCreateRoutes = "create_routes";
    public const string OperationDeleteRoutes = "delete_routes";

    // prefix operations
    public const string OperationPrefixUpdateCheck = "prefix_update_check";
    public const string OperationPrefixHttpHead = "prefix_http_head";
    public const string OperationPrefixHttpGet = "prefix_http_get";
    public const string OperationPrefixCompare = "prefix_compare";
    public const string OperationPrefixPersistMetadata = "prefix_persist_metadata";

    // dns / custom-route operations
    public const string OperationCustomRouteRefresh = "custom_route_refresh";
    public const string OperationDnsCacheRead = "dns_cache_read";
    public const string OperationDnsResolve = "dns_resolve";
    public const string OperationDnsCacheWrite = "dns_cache_write";

    // ipc operations
    public const string OperationIpcRequest = "ipc_request";
    public const string OperationIpcConnect = "ipc_connect";
    public const string OperationIpcSend = "ipc_send";
    public const string OperationIpcReceive = "ipc_receive";
    public const string OperationIpcDispatch = "ipc_dispatch";

    // support export operations
    public const string OperationSupportSnapshotExport = "support_snapshot_export";
    public const string OperationSupportBundleExport = "support_bundle_export";
    public const string OperationSupportCaptureSnapshot = "support_capture_snapshot";
    public const string OperationSupportSerialize = "support_serialize";
    public const string OperationSupportWriteJson = "support_write_json";
    public const string OperationSupportCreateZip = "support_create_zip";

    // trigger
    public const string TriggerScheduled = "scheduled";
    public const string TriggerForced = "forced";
    public const string TriggerCli = "cli";
    public const string TriggerTray = "tray";
    public const string TriggerStartup = "startup";
    public const string TriggerRepair = "repair";
    public const string TriggerUnknown = "unknown";

    // route_kind
    public const string RouteKindPrefix = "prefix";
    public const string RouteKindEndpoint = "endpoint";
    public const string RouteKindUnknown = "unknown";

    // change_kind
    public const string ChangeKindCreate = "create";
    public const string ChangeKindDelete = "delete";
    public const string ChangeKindUnknown = "unknown";

    // source
    public const string SourceOfficial = "official";
    public const string SourceCustom = "custom";
    public const string SourceCache = "cache";
    public const string SourceUnknown = "unknown";

    // cache_state
    public const string CacheFresh = "fresh";
    public const string CacheStale = "stale";
    public const string CacheMiss = "miss";
    public const string CacheFailed = "failed";
    public const string CacheUnknown = "unknown";

    // diagnostic_severity
    public const string SeverityPass = "pass";
    public const string SeverityWarning = "warning";
    public const string SeverityFailure = "failure";
    public const string SeverityUnknown = "unknown";

    // service_state
    public const string ServiceEnabledState = "enabled";
    public const string ServiceDisabledState = "disabled";
    public const string ServiceUnknown = "unknown";

    // failure_category
    public const string FailureIo = "io";
    public const string FailureTimeout = "timeout";
    public const string FailureCancellation = "cancellation";
    public const string FailureHttp = "http";
    public const string FailureDns = "dns";
    public const string FailureRouting = "routing";
    public const string FailureSerialization = "serialization";
    public const string FailureInvalidResponse = "invalid_response";
    public const string FailureUnknown = "unknown";
}
