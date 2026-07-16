using System.Net;
using IranDirect.Core.Models;
using IranDirect.Core.Routing;

namespace IranDirect.Core.Vpn;

public sealed class VpnEndpointRouteManager
{
    private const int EndpointMetric = 1;

    private readonly IRouteManager _routeManager;

    public VpnEndpointRouteManager(
        IRouteManager routeManager)
    {
        _routeManager = routeManager;
    }

    public async Task<VpnEndpointProtectionResult>
        EnsureProtectedAsync(
            IReadOnlyCollection<ResolvedVpnEndpoint> endpoints,
            DirectGateway gateway,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(gateway);

        ManagedRoute[] desired = endpoints
            .Select(endpoint =>
                CreateRoute(endpoint, gateway))
            .GroupBy(
                route => route.Identity,
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();

        if (desired.Length == 0)
        {
            throw new InvalidOperationException(
                "No IPv4 VPN endpoints are available for protection.");
        }

        IReadOnlyList<SystemRoute> before =
            await _routeManager.GetIpv4RoutesAsync(
                cancellationToken);

        HashSet<string> existingIdentities = before
            .Select(ToIdentity)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        ManagedRoute[] missing = desired
            .Where(route =>
                !existingIdentities.Contains(route.Identity))
            .ToArray();

        await _routeManager.AddRoutesAsync(
            missing,
            cancellationToken);

        IReadOnlyList<SystemRoute> after =
            await _routeManager.GetIpv4RoutesAsync(
                cancellationToken);

        HashSet<string> verifiedIdentities = after
            .Select(ToIdentity)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        ManagedRoute[] unprotected = desired
            .Where(route =>
                !verifiedIdentities.Contains(route.Identity))
            .ToArray();

        if (unprotected.Length > 0)
        {
            string endpointsText = string.Join(
                ", ",
                unprotected.Select(
                    route => route.DestinationPrefix));

            throw new InvalidOperationException(
                "VPN endpoint protection could not be verified for: " +
                endpointsText);
        }

        return new VpnEndpointProtectionResult
        {
            ProtectedRoutes = desired,
            AddedRouteIdentities = missing
                .Select(route => route.Identity)
                .ToHashSet(
                    StringComparer.OrdinalIgnoreCase)
        };
    }

    public async Task<VpnEndpointProtectionHealth>
        GetHealthAsync(
            IReadOnlyCollection<VpnEndpointInventoryItem> endpoints,
            CancellationToken cancellationToken = default)
    {
        VpnEndpointInventoryItem[] current = endpoints
            .Where(endpoint => endpoint.IsCurrent)
            .ToArray();

        if (current.Length == 0)
        {
            return new VpnEndpointProtectionHealth();
        }

        IReadOnlyList<SystemRoute> routes =
            await _routeManager.GetIpv4RoutesAsync(
                cancellationToken);

        HashSet<string> identities = routes
            .Select(ToIdentity)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        int protectedCount = current.Count(
            endpoint =>
                identities.Contains(endpoint.Identity));

        return new VpnEndpointProtectionHealth
        {
            CurrentEndpointCount = current.Length,
            ProtectedEndpointCount = protectedCount
        };
    }

    private static ManagedRoute CreateRoute(
        ResolvedVpnEndpoint endpoint,
        DirectGateway gateway)
    {
        if (!IPAddress.TryParse(
                endpoint.Address,
                out IPAddress? address))
        {
            throw new InvalidOperationException(
                $"VPN endpoint address is invalid: " +
                $"{endpoint.Address}");
        }

        return new ManagedRoute
        {
            DestinationPrefix = $"{address}/32",
            Gateway = gateway.Address,
            InterfaceIndex = gateway.InterfaceIndex,
            Metric = EndpointMetric
        };
    }

    private static string ToIdentity(
        SystemRoute route) =>
        $"{route.DestinationPrefix}|" +
        $"{route.NextHop}|" +
        $"{route.InterfaceIndex}";
}