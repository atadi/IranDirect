namespace IranDirect.Core.Observability.Telemetry;

/// <summary>
/// Bounded, enumerated tag values for the approved telemetry tags. Every value
/// here MUST belong to a closed set; free-form values are never permitted.
/// </summary>
public static class IranDirectTagValues
{
    // outcome
    public const string Success = "success";
    public const string Failure = "failure";
    public const string Cancelled = "cancelled";
    public const string Timeout = "timeout";
    public const string NoChange = "no_change";
    public const string OutcomeUnknown = "unknown";

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
