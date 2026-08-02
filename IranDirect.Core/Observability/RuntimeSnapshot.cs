namespace IranDirect.Core.Observability;

using IranDirect.Core.Configuration;
using IranDirect.Core.CustomRoutes;
using IranDirect.Core.Prefixes;
using IranDirect.Core.Runtime.Execution;
using IranDirect.Core.Runtime.Profiling;
using IranDirect.Core.Vpn;

public sealed record RuntimeSnapshot
{
    public DateTimeOffset CapturedAt { get; init; }

    public int SchemaVersion { get; init; } = 1;

    public DesiredConfiguration? Configuration { get; init; }

    public IranDirectStatus? Runtime { get; init; }

    public RuntimeOperationSnapshot? Operation { get; init; }

    public PrefixSourceMetadata? PrefixSource { get; init; }

    public PrefixUpdateCheckResult? PrefixUpdate { get; init; }

    public PrefixUpdateMonitorSnapshot? PrefixUpdateMonitor
        { get; init; }

    public int? PrefixCount { get; init; }

    public int? InstalledRouteCount { get; init; }

    public int? RouteInventoryCount { get; init; }

    public VpnEndpointProtectionHealth? VpnEndpointHealth { get; init; }

    public IReadOnlyList<CustomRouteDnsCacheStatus> DnsCache
        { get; init; } = [];

    public RuntimeCyclePerfReport? Performance { get; init; }

    public string? LastError { get; init; }

    public string? LastWarning { get; init; }
}
