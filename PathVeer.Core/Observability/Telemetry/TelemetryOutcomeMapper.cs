using PathVeer.Core.CustomRoutes;
using PathVeer.Core.Diagnostics;
using PathVeer.Core.Ipc;
using PathVeer.Core.Runtime.Execution;
using PathVeer.Core.Runtime.Profiling;

namespace PathVeer.Core.Observability.Telemetry;

/// <summary>
/// Centralized, exhaustive mapping from existing domain enums to bounded
/// telemetry values. Every branch is a switch expression with an explicit
/// fallback; no <c>ToString()</c> is used as a mapping source and no
/// culture-sensitive conversion occurs.
/// </summary>
public static class TelemetryOutcomeMapper
{
    public static string ToOutcomeString(TelemetryOutcome outcome) =>
        outcome switch
        {
            TelemetryOutcome.Success => IranDirectTagValues.Success,
            TelemetryOutcome.Failure => IranDirectTagValues.Failure,
            TelemetryOutcome.Cancelled => IranDirectTagValues.Cancelled,
            TelemetryOutcome.Timeout => IranDirectTagValues.Timeout,
            TelemetryOutcome.NoChange => IranDirectTagValues.NoChange,
            _ => IranDirectTagValues.OutcomeUnknown,
        };

    /// <summary>
    /// Maps from <see cref="RuntimeExecutionResultStatus"/>. Partially-completed
    /// is treated as <see cref="TelemetryOutcome.Failure"/> per the Phase 32.1
    /// rule (no new bounded value introduced this phase).
    /// </summary>
    public static TelemetryOutcome Map(RuntimeExecutionResultStatus status) =>
        status switch
        {
            RuntimeExecutionResultStatus.Completed => TelemetryOutcome.Success,
            RuntimeExecutionResultStatus.NoExecutionRequired =>
                TelemetryOutcome.NoChange,
            RuntimeExecutionResultStatus.Planned => TelemetryOutcome.Success,
            RuntimeExecutionResultStatus.Failed => TelemetryOutcome.Failure,
            RuntimeExecutionResultStatus.Cancelled => TelemetryOutcome.Cancelled,
            RuntimeExecutionResultStatus.PartiallyCompleted =>
                TelemetryOutcome.Failure,
            _ => TelemetryOutcome.Unknown,
        };

    /// <summary>
    /// Maps from the persisted-cycle completion status.
    /// </summary>
    public static TelemetryOutcome Map(CycleCompletionStatus status) =>
        status switch
        {
            CycleCompletionStatus.Completed => TelemetryOutcome.Success,
            CycleCompletionStatus.PartiallyCompleted =>
                TelemetryOutcome.Failure,
            CycleCompletionStatus.Failed => TelemetryOutcome.Failure,
            CycleCompletionStatus.Cancelled => TelemetryOutcome.Cancelled,
            _ => TelemetryOutcome.Unknown,
        };

    /// <summary>
    /// <see cref="DiagnosticSeverity"/> has more members (Info, Error) than the
    /// bounded <c>diagnostic_severity</c> set (pass/warning/failure). Mapped
    /// conservatively: Info -> pass, Error -> failure.
    /// </summary>
    public static string Map(DiagnosticSeverity severity) =>
        severity switch
        {
            DiagnosticSeverity.Pass => IranDirectTagValues.SeverityPass,
            DiagnosticSeverity.Info => IranDirectTagValues.SeverityPass,
            DiagnosticSeverity.Warning => IranDirectTagValues.SeverityWarning,
            DiagnosticSeverity.Fail => IranDirectTagValues.SeverityFailure,
            DiagnosticSeverity.Error => IranDirectTagValues.SeverityFailure,
            _ => IranDirectTagValues.SeverityUnknown,
        };

    /// <summary>
    /// Maps the DNS cache state to the bounded <c>cache_state</c> tag.
    /// Expired/Missing/Disabled collapse to the bounded set; Missing and
    /// Disabled map to <c>miss</c> and <c>failed</c> respectively (documented
    /// conservative choice — both indicate no usable cached value).
    /// </summary>
    public static string Map(CustomRouteDnsCacheState state) =>
        state switch
        {
            CustomRouteDnsCacheState.Fresh => IranDirectTagValues.CacheFresh,
            CustomRouteDnsCacheState.Stale => IranDirectTagValues.CacheStale,
            CustomRouteDnsCacheState.Expired => IranDirectTagValues.CacheMiss,
            CustomRouteDnsCacheState.Failed => IranDirectTagValues.CacheFailed,
            CustomRouteDnsCacheState.Missing => IranDirectTagValues.CacheMiss,
            CustomRouteDnsCacheState.Disabled => IranDirectTagValues.CacheFailed,
            _ => IranDirectTagValues.CacheUnknown,
        };

    /// <summary>
    /// Maps an IPC command enum to the bounded <c>ipc_command</c> tag value.
    /// The tag value is the enum member name exactly; this is the centralized
    /// mapper so callers never embed the enum name elsewhere.
    /// </summary>
    public static string Map(PathVeerCommand command) => command switch
    {
        PathVeerCommand.Status => nameof(PathVeerCommand.Status),
        PathVeerCommand.UpdatePrefixes =>
            nameof(PathVeerCommand.UpdatePrefixes),
        PathVeerCommand.Enable => nameof(PathVeerCommand.Enable),
        PathVeerCommand.Disable => nameof(PathVeerCommand.Disable),
        PathVeerCommand.Repair => nameof(PathVeerCommand.Repair),
        PathVeerCommand.VpnEndpoints => nameof(PathVeerCommand.VpnEndpoints),
        PathVeerCommand.Diagnostics => nameof(PathVeerCommand.Diagnostics),
        PathVeerCommand.GetConfiguration =>
            nameof(PathVeerCommand.GetConfiguration),
        PathVeerCommand.SetConfigurationEnabled =>
            nameof(PathVeerCommand.SetConfigurationEnabled),
        PathVeerCommand.SetConfigurationProfilePath =>
            nameof(PathVeerCommand.SetConfigurationProfilePath),
        PathVeerCommand.SetConfigurationDirectCountry =>
            nameof(PathVeerCommand.SetConfigurationDirectCountry),
        PathVeerCommand.RuntimePlan => nameof(PathVeerCommand.RuntimePlan),
        PathVeerCommand.CustomRoutesList =>
            nameof(PathVeerCommand.CustomRoutesList),
        PathVeerCommand.CustomRoutesAddDomain =>
            nameof(PathVeerCommand.CustomRoutesAddDomain),
        PathVeerCommand.CustomRoutesAddIp =>
            nameof(PathVeerCommand.CustomRoutesAddIp),
        PathVeerCommand.CustomRoutesAddCidr =>
            nameof(PathVeerCommand.CustomRoutesAddCidr),
        PathVeerCommand.CustomRoutesEnable =>
            nameof(PathVeerCommand.CustomRoutesEnable),
        PathVeerCommand.CustomRoutesDisable =>
            nameof(PathVeerCommand.CustomRoutesDisable),
        PathVeerCommand.CustomRoutesRemove =>
            nameof(PathVeerCommand.CustomRoutesRemove),
        PathVeerCommand.CustomRoutesResolve =>
            nameof(PathVeerCommand.CustomRoutesResolve),
        PathVeerCommand.CustomRoutesCacheStatus =>
            nameof(PathVeerCommand.CustomRoutesCacheStatus),
        PathVeerCommand.CustomRoutesInvalidateCache =>
            nameof(PathVeerCommand.CustomRoutesInvalidateCache),
        PathVeerCommand.CustomRoutesInvalidateAllCaches =>
            nameof(PathVeerCommand.CustomRoutesInvalidateAllCaches),
        PathVeerCommand.RuntimeSnapshot =>
            nameof(PathVeerCommand.RuntimeSnapshot),
        PathVeerCommand.PrefixUpdateCheckNow =>
            nameof(PathVeerCommand.PrefixUpdateCheckNow),
        PathVeerCommand.ExecutionPreview =>
            nameof(PathVeerCommand.ExecutionPreview),
        PathVeerCommand.SupportBundleExport =>
            nameof(PathVeerCommand.SupportBundleExport),
        _ => IranDirectTagValues.OutcomeUnknown,
    };
}
