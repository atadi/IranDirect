using System.Collections.Generic;

namespace PathVeer.Core.Observability.Telemetry;

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
        PathVeerTagNames.Operation,
        PathVeerTagNames.Outcome,
        PathVeerTagNames.Trigger,
        PathVeerTagNames.RouteKind,
        PathVeerTagNames.ChangeKind,
        PathVeerTagNames.Source,
        PathVeerTagNames.CacheState,
        PathVeerTagNames.IpcCommand,
        PathVeerTagNames.DiagnosticSeverity,
        PathVeerTagNames.ServiceState,
        PathVeerTagNames.FailureCategory,
    };

    private static readonly HashSet<string> s_prohibited = new(
        PathVeerTagNames.Prohibited);

    // Bounded value sets per approved tag. Kept as explicit membership sets so
    // validation is O(1) and requires no reflection.
    private static readonly Dictionary<string, HashSet<string>> s_values = new()
    {
        [PathVeerTagNames.Outcome] = new()
        {
            PathVeerTagValues.Success,
            PathVeerTagValues.Failure,
            PathVeerTagValues.Cancelled,
            PathVeerTagValues.Timeout,
            PathVeerTagValues.NoChange,
            PathVeerTagValues.OutcomeUnknown,
        },
        [PathVeerTagNames.Trigger] = new()
        {
            PathVeerTagValues.TriggerScheduled,
            PathVeerTagValues.TriggerForced,
            PathVeerTagValues.TriggerCli,
            PathVeerTagValues.TriggerTray,
            PathVeerTagValues.TriggerStartup,
            PathVeerTagValues.TriggerRepair,
            PathVeerTagValues.TriggerUnknown,
        },
        [PathVeerTagNames.RouteKind] = new()
        {
            PathVeerTagValues.RouteKindPrefix,
            PathVeerTagValues.RouteKindEndpoint,
            PathVeerTagValues.RouteKindUnknown,
        },
        [PathVeerTagNames.ChangeKind] = new()
        {
            PathVeerTagValues.ChangeKindCreate,
            PathVeerTagValues.ChangeKindDelete,
            PathVeerTagValues.ChangeKindUnknown,
        },
        [PathVeerTagNames.Source] = new()
        {
            PathVeerTagValues.SourceOfficial,
            PathVeerTagValues.SourceCustom,
            PathVeerTagValues.SourceCache,
            PathVeerTagValues.SourceUnknown,
        },
        [PathVeerTagNames.CacheState] = new()
        {
            PathVeerTagValues.CacheFresh,
            PathVeerTagValues.CacheStale,
            PathVeerTagValues.CacheMiss,
            PathVeerTagValues.CacheFailed,
            PathVeerTagValues.CacheUnknown,
        },
        [PathVeerTagNames.DiagnosticSeverity] = new()
        {
            PathVeerTagValues.SeverityPass,
            PathVeerTagValues.SeverityWarning,
            PathVeerTagValues.SeverityFailure,
            PathVeerTagValues.SeverityUnknown,
        },
        [PathVeerTagNames.ServiceState] = new()
        {
            PathVeerTagValues.ServiceEnabledState,
            PathVeerTagValues.ServiceDisabledState,
            PathVeerTagValues.ServiceUnknown,
        },
        [PathVeerTagNames.FailureCategory] = new()
        {
            PathVeerTagValues.FailureIo,
            PathVeerTagValues.FailureTimeout,
            PathVeerTagValues.FailureCancellation,
            PathVeerTagValues.FailureHttp,
            PathVeerTagValues.FailureDns,
            PathVeerTagValues.FailureRouting,
            PathVeerTagValues.FailureSerialization,
            PathVeerTagValues.FailureInvalidResponse,
            PathVeerTagValues.FailureUnknown,
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
