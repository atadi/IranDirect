using IranDirect.Core.CustomRoutes;
using IranDirect.Core.Runtime;
using IranDirect.Core.Runtime.Execution;
using IranDirect.Core.Configuration;
using IranDirect.Core.Diagnostics;
using IranDirect.Core.Routing;
using IranDirect.Core.Vpn;
using IranDirect.Core.Observability;

namespace IranDirect.Core.Ipc;

public sealed record ServiceResponse
{
    public bool Success { get; init; }

    public string Message { get; init; } = "";

    public string? ErrorCode { get; init; }

    public IranDirectStatus? Status { get; init; }

    public ReconciliationResult? Reconciliation { get; init; }

    public int? PrefixCount { get; init; }

    public IReadOnlyList<ResolvedVpnEndpoint> VpnEndpoints
        { get; init; } = [];

    public IranDirectDiagnostics? Diagnostics { get; init; }

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
}