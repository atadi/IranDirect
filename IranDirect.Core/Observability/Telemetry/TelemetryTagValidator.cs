using System.Collections.Generic;

namespace IranDirect.Core.Observability.Telemetry;

/// <summary>
/// Testable catalog answering whether a tag name is approved, prohibited, or
/// which values are valid for an approved bounded tag. Matching is
/// case-sensitive and exact; no normalization of arbitrary input occurs. This
/// is a static catalog for tests and future instrumentation helpers only — it
/// is not placed in any hot path.
/// </summary>
public static class TelemetryTagValidator
{
    private static readonly HashSet<string> s_approved = new()
    {
        IranDirectTagNames.Operation,
        IranDirectTagNames.Outcome,
        IranDirectTagNames.Trigger,
        IranDirectTagNames.RouteKind,
        IranDirectTagNames.ChangeKind,
        IranDirectTagNames.Source,
        IranDirectTagNames.CacheState,
        IranDirectTagNames.IpcCommand,
        IranDirectTagNames.DiagnosticSeverity,
        IranDirectTagNames.ServiceState,
        IranDirectTagNames.FailureCategory,
    };

    private static readonly HashSet<string> s_prohibited = new(
        IranDirectTagNames.Prohibited);

    // Bounded value sets per approved tag. Kept as explicit membership sets so
    // validation is O(1) and requires no reflection.
    private static readonly Dictionary<string, HashSet<string>> s_values = new()
    {
        [IranDirectTagNames.Outcome] = new()
        {
            IranDirectTagValues.Success,
            IranDirectTagValues.Failure,
            IranDirectTagValues.Cancelled,
            IranDirectTagValues.Timeout,
            IranDirectTagValues.NoChange,
            IranDirectTagValues.OutcomeUnknown,
        },
        [IranDirectTagNames.Trigger] = new()
        {
            IranDirectTagValues.TriggerScheduled,
            IranDirectTagValues.TriggerForced,
            IranDirectTagValues.TriggerCli,
            IranDirectTagValues.TriggerTray,
            IranDirectTagValues.TriggerStartup,
            IranDirectTagValues.TriggerRepair,
            IranDirectTagValues.TriggerUnknown,
        },
        [IranDirectTagNames.RouteKind] = new()
        {
            IranDirectTagValues.RouteKindPrefix,
            IranDirectTagValues.RouteKindEndpoint,
            IranDirectTagValues.RouteKindUnknown,
        },
        [IranDirectTagNames.ChangeKind] = new()
        {
            IranDirectTagValues.ChangeKindCreate,
            IranDirectTagValues.ChangeKindDelete,
            IranDirectTagValues.ChangeKindUnknown,
        },
        [IranDirectTagNames.Source] = new()
        {
            IranDirectTagValues.SourceOfficial,
            IranDirectTagValues.SourceCustom,
            IranDirectTagValues.SourceCache,
            IranDirectTagValues.SourceUnknown,
        },
        [IranDirectTagNames.CacheState] = new()
        {
            IranDirectTagValues.CacheFresh,
            IranDirectTagValues.CacheStale,
            IranDirectTagValues.CacheMiss,
            IranDirectTagValues.CacheFailed,
            IranDirectTagValues.CacheUnknown,
        },
        [IranDirectTagNames.DiagnosticSeverity] = new()
        {
            IranDirectTagValues.SeverityPass,
            IranDirectTagValues.SeverityWarning,
            IranDirectTagValues.SeverityFailure,
            IranDirectTagValues.SeverityUnknown,
        },
        [IranDirectTagNames.ServiceState] = new()
        {
            IranDirectTagValues.ServiceEnabledState,
            IranDirectTagValues.ServiceDisabledState,
            IranDirectTagValues.ServiceUnknown,
        },
        [IranDirectTagNames.FailureCategory] = new()
        {
            IranDirectTagValues.FailureIo,
            IranDirectTagValues.FailureTimeout,
            IranDirectTagValues.FailureCancellation,
            IranDirectTagValues.FailureHttp,
            IranDirectTagValues.FailureDns,
            IranDirectTagValues.FailureRouting,
            IranDirectTagValues.FailureSerialization,
            IranDirectTagValues.FailureInvalidResponse,
            IranDirectTagValues.FailureUnknown,
        },
    };

    public static bool IsApprovedTagName(string name) =>
        s_approved.Contains(name);

    public static bool IsProhibitedTagName(string name) =>
        s_prohibited.Contains(name);

    /// <summary>
    /// True when <paramref name="name"/> is approved and <paramref name="value"/>
    /// belongs to its bounded set. Tags whose values are not centrally bounded
    /// in this phase (operation, ipc_command) return false here; those are
    /// validated by their dedicated mappers instead.
    /// </summary>
    public static bool IsValidBoundedValue(string name, string value) =>
        s_values.TryGetValue(name, out HashSet<string>? allowed) &&
        allowed.Contains(value);

    public static IReadOnlyCollection<string> ApprovedTagNames =>
        s_approved.ToArray();

    public static IReadOnlyCollection<string> ProhibitedTagNames =>
        s_prohibited.ToArray();
}
