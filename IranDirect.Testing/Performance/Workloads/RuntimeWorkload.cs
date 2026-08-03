using IranDirect.Core.Routing;
using IranDirect.Core.Runtime;
using IranDirect.Core.Vpn;

namespace IranDirect.Testing.Performance.Workloads;

public sealed record RuntimeWorkload
{
    public required RuntimeWorkloadScenario Scenario { get; init; }

    public required RuntimeWorkloadSize Size { get; init; }

    public required int Seed { get; init; }

    public required string GeneratorVersion { get; init; }

    public required IReadOnlyList<DesiredPrefixRoute> DesiredPrefixes { get; init; }

    public required IReadOnlyList<ObservedRoute> ObservedRoutes { get; init; }

    public required RouteInventory RouteInventory { get; init; }

    public required IReadOnlyList<DesiredEndpointRoute> DesiredEndpointRoutes { get; init; }

    public required IReadOnlyList<ObservedVpnEndpoint> VpnEndpoints { get; init; }

    public required VpnEndpointInventory VpnEndpointInventory { get; init; }

    public required int ExpectedAddedCount { get; init; }

    public required int ExpectedRemovedCount { get; init; }

    public required int ExpectedUnchangedCount { get; init; }
}
