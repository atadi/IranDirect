namespace PathVeer.Core.Observability;

using PathVeer.Core.Configuration;
using PathVeer.Core.CustomRoutes;
using PathVeer.Core.Prefixes;
using PathVeer.Core.Runtime.Execution;
using PathVeer.Core.Runtime.Profiling;
using PathVeer.Core.Vpn;

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
