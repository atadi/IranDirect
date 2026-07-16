using IranDirect.Core.Diagnostics;
using IranDirect.Core.Routing;
using IranDirect.Core.Vpn;

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
}