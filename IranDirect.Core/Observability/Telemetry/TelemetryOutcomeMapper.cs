using IranDirect.Core.CustomRoutes;
using IranDirect.Core.Diagnostics;
using IranDirect.Core.Ipc;
using IranDirect.Core.Runtime.Execution;
using IranDirect.Core.Runtime.Profiling;

namespace IranDirect.Core.Observability.Telemetry;

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
    public static string Map(IranDirectCommand command) => command switch
    {
        IranDirectCommand.Status => nameof(IranDirectCommand.Status),
        IranDirectCommand.UpdatePrefixes =>
            nameof(IranDirectCommand.UpdatePrefixes),
        IranDirectCommand.Enable => nameof(IranDirectCommand.Enable),
        IranDirectCommand.Disable => nameof(IranDirectCommand.Disable),
        IranDirectCommand.Repair => nameof(IranDirectCommand.Repair),
        IranDirectCommand.VpnEndpoints => nameof(IranDirectCommand.VpnEndpoints),
        IranDirectCommand.Diagnostics => nameof(IranDirectCommand.Diagnostics),
        IranDirectCommand.GetConfiguration =>
            nameof(IranDirectCommand.GetConfiguration),
        IranDirectCommand.SetConfigurationEnabled =>
            nameof(IranDirectCommand.SetConfigurationEnabled),
        IranDirectCommand.SetConfigurationProfilePath =>
            nameof(IranDirectCommand.SetConfigurationProfilePath),
        IranDirectCommand.RuntimePlan => nameof(IranDirectCommand.RuntimePlan),
        IranDirectCommand.CustomRoutesList =>
            nameof(IranDirectCommand.CustomRoutesList),
        IranDirectCommand.CustomRoutesAddDomain =>
            nameof(IranDirectCommand.CustomRoutesAddDomain),
        IranDirectCommand.CustomRoutesAddIp =>
            nameof(IranDirectCommand.CustomRoutesAddIp),
        IranDirectCommand.CustomRoutesAddCidr =>
            nameof(IranDirectCommand.CustomRoutesAddCidr),
        IranDirectCommand.CustomRoutesEnable =>
            nameof(IranDirectCommand.CustomRoutesEnable),
        IranDirectCommand.CustomRoutesDisable =>
            nameof(IranDirectCommand.CustomRoutesDisable),
        IranDirectCommand.CustomRoutesRemove =>
            nameof(IranDirectCommand.CustomRoutesRemove),
        IranDirectCommand.CustomRoutesResolve =>
            nameof(IranDirectCommand.CustomRoutesResolve),
        IranDirectCommand.CustomRoutesCacheStatus =>
            nameof(IranDirectCommand.CustomRoutesCacheStatus),
        IranDirectCommand.CustomRoutesInvalidateCache =>
            nameof(IranDirectCommand.CustomRoutesInvalidateCache),
        IranDirectCommand.CustomRoutesInvalidateAllCaches =>
            nameof(IranDirectCommand.CustomRoutesInvalidateAllCaches),
        IranDirectCommand.RuntimeSnapshot =>
            nameof(IranDirectCommand.RuntimeSnapshot),
        IranDirectCommand.PrefixUpdateCheckNow =>
            nameof(IranDirectCommand.PrefixUpdateCheckNow),
        IranDirectCommand.ExecutionPreview =>
            nameof(IranDirectCommand.ExecutionPreview),
        IranDirectCommand.SupportBundleExport =>
            nameof(IranDirectCommand.SupportBundleExport),
        _ => IranDirectTagValues.OutcomeUnknown,
    };
}
