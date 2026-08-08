using PathVeer.Core.CustomRoutes;
using PathVeer.Core.Planning;
using PathVeer.Core.Runtime;
using PathVeer.Core.Runtime.Execution;
using PathVeer.Core.Configuration;
using PathVeer.Core.Diagnostics;
using PathVeer.Core.Routing;
using PathVeer.Core.Vpn;
using PathVeer.Core.Observability;
using PathVeer.Core.Prefixes;

namespace PathVeer.Core.Ipc;

public sealed record ServiceResponse
{
    public bool Success { get; init; }

    public string Message { get; init; } = "";

    public string? ErrorCode { get; init; }

    public PathVeerStatus? Status { get; init; }

    public ReconciliationResult? Reconciliation { get; init; }

    public int? PrefixCount { get; init; }

    public IReadOnlyList<ResolvedVpnEndpoint> VpnEndpoints
        { get; init; } = [];

    public PathVeerDiagnostics? Diagnostics { get; init; }

    public DesiredConfiguration? Configuration { get; init; }

    public RuntimePlanSnapshot? RuntimePlan { get; init; }

    public RuntimeDecision? Decision { get; init; }

    public RuntimeExecutionResult? Execution { get; init; }

    public IReadOnlyList<CustomRouteEntry> CustomRoutes
        { get; init; } = [];

    public CustomRouteResolutionResult?
        CustomRouteResolution { get; init; }

    public IReadOnlyList<CustomRouteDnsCacheStatus>
        CustomRouteDnsCacheStatuses { get; init; } = [];

    public RuntimeSnapshot? Snapshot { get; init; }

    public PrefixUpdateMonitorSnapshot? PrefixUpdateMonitor
        { get; init; }

    public DiagnosticReport? Report { get; init; }

    public ExecutionPreview? Preview { get; init; }

    public string? SupportBundlePath { get; init; }

    public long? SupportBundleBytesWritten { get; init; }

    /// <summary>
    /// When a country-set command also triggered a prefix refresh, reports
    /// whether the dataset update succeeded. <c>null</c> when the response is
    /// unrelated to a country set. Lets the CLI distinguish a partial set
    /// (config accepted, dataset unavailable) from a full success.
    /// </summary>
    public bool? PrefixRefreshed { get; init; }
}