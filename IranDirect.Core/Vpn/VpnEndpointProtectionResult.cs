using IranDirect.Core.Routing;

namespace IranDirect.Core.Vpn;

public sealed record VpnEndpointProtectionResult
{
    public IReadOnlyList<ManagedRoute> ProtectedRoutes
        { get; init; } = [];

    public IReadOnlySet<string> AddedRouteIdentities
        { get; init; } = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
}