using System.Net;

namespace PathVeer.Core.Routing;

public sealed class RouteReconciler
{
    private readonly IRouteManager _routeManager;

    public RouteReconciler(IRouteManager routeManager)
    {
        _routeManager = routeManager;
    }

    public async Task<ReconciliationResult> EnableAsync(
        IReadOnlyCollection<string> prefixes,
        IPAddress gateway,
        uint interfaceIndex,
        int metric = 5,
        CancellationToken cancellationToken = default)
    {
        ManagedRoute[] desired = prefixes
            .Select(prefix => new ManagedRoute
            {
                DestinationPrefix = prefix,
                Gateway = gateway,
                InterfaceIndex = interfaceIndex,
                Metric = metric
            })
            .ToArray();

        IReadOnlyList<SystemRoute> actual =
            await _routeManager.GetIpv4RoutesAsync(
                cancellationToken);

        HashSet<string> existingIdentities = actual
            .Select(ToIdentity)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        ManagedRoute[] missing = desired
            .Where(route =>
                !existingIdentities.Contains(route.Identity))
            .ToArray();

        await _routeManager.AddRoutesAsync(
            missing,
            cancellationToken);

        return new ReconciliationResult
        {
            DesiredCount = desired.Length,
            ExistingCount = desired.Length - missing.Length,
            AddedCount = missing.Length,
            AddedRouteIdentities = missing
                .Select(route => route.Identity)
                .ToArray()
        };
    }

    public async Task<ReconciliationResult> DisableAsync(
        IReadOnlyCollection<ManagedRoute> managedRoutes,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<SystemRoute> actual =
            await _routeManager.GetIpv4RoutesAsync(
                cancellationToken);

        HashSet<string> actualIdentities = actual
            .Select(ToIdentity)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        ManagedRoute[] matching = managedRoutes
            .Where(route =>
                actualIdentities.Contains(route.Identity))
            .ToArray();

        await _routeManager.DeleteRoutesAsync(
            matching,
            cancellationToken);

        return new ReconciliationResult
        {
            DesiredCount = managedRoutes.Count,
            ExistingCount = matching.Length,
            RemovedCount = matching.Length
        };
    }

    public async Task<int> CountMatchingRoutesAsync(
        IReadOnlyCollection<ManagedRoute> managedRoutes,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<SystemRoute> actual =
            await _routeManager.GetIpv4RoutesAsync(
                cancellationToken);

        HashSet<string> actualIdentities = actual
            .Select(ToIdentity)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return managedRoutes.Count(route =>
            actualIdentities.Contains(route.Identity));
    }

    public async Task<int> CountMatchingRoutesAsync(
        IReadOnlyCollection<string> prefixes,
        IPAddress gateway,
        uint interfaceIndex,
        CancellationToken cancellationToken = default)
    {
        ManagedRoute[] routes = prefixes
            .Select(prefix => new ManagedRoute
            {
                DestinationPrefix = prefix,
                Gateway = gateway,
                InterfaceIndex = interfaceIndex
            })
            .ToArray();

        return await CountMatchingRoutesAsync(
            routes,
            cancellationToken);
    }

    private static string ToIdentity(
        SystemRoute route) =>
        $"{route.DestinationPrefix}|" +
        $"{route.NextHop}|" +
        $"{route.InterfaceIndex}";
}