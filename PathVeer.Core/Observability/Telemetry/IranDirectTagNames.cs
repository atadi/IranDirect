namespace PathVeer.Core.Observability.Telemetry;

/// <summary>
/// Approved telemetry tag names (lower_snake_case) and the prohibited tag
/// names that must never appear on spans or metrics. The prohibited set exists
/// so architectural tests can assert telemetry contracts never leak
/// sensitive, high-cardinality fields.
/// </summary>
public static class IranDirectTagNames
{
    // Approved, bounded tag names
    public const string Operation = "operation";
    public const string Outcome = "outcome";
    public const string Trigger = "trigger";
    public const string RouteKind = "route_kind";
    public const string ChangeKind = "change_kind";
    public const string Source = "source";
    public const string CacheState = "cache_state";
    public const string IpcCommand = "ipc_command";
    public const string DiagnosticSeverity = "diagnostic_severity";
    public const string ServiceState = "service_state";
    public const string FailureCategory = "failure_category";

    /// <summary>
    /// Sensitive / high-cardinality tag names that must never be added to
    /// telemetry. These are deliberately absent from the approved set and are
    /// provided so tests can scan for accidental leakage.
    /// </summary>
    public static readonly IReadOnlyList<string> Prohibited =
    [
        "destination_prefix",
        "gateway",
        "next_hop",
        "interface_index",
        "interface_name",
        "domain",
        "dns_domain",
        "output_path",
        "file_path",
        "pipe_payload",
        "machine_name",
        "user_name",
        "exception_message",
        "url",
        "endpoint",
        "route_identity",
        "diagnostic_id",
        "execution_step_identity",
    ];
}
