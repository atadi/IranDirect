using System.Net;

namespace IranDirect.Core.Routing;

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
            AddedCount = missing.Length
        };
    }

    public async Task<ReconciliationResult> DisableAsync(
        IReadOnlyCollection<string> prefixes,
        IPAddress gateway,
        uint interfaceIndex,
        CancellationToken cancellationToken = default)
    {
        HashSet<string> managedPrefixes =
            prefixes.ToHashSet(
                StringComparer.OrdinalIgnoreCase);

        IReadOnlyList<SystemRoute> actual =
            await _routeManager.GetIpv4RoutesAsync(
                cancellationToken);

        ManagedRoute[] matching = actual
            .Where(route =>
                managedPrefixes.Contains(
                    route.DestinationPrefix))
            .Where(route =>
                route.InterfaceIndex == interfaceIndex)
            .Where(route =>
                route.NextHop.Equals(gateway))
            .Select(route => new ManagedRoute
            {
                DestinationPrefix =
                    route.DestinationPrefix,
                Gateway = route.NextHop,
                InterfaceIndex =
                    route.InterfaceIndex,
                Metric = route.RouteMetric
            })
            .ToArray();

        await _routeManager.DeleteRoutesAsync(
            matching,
            cancellationToken);

        return new ReconciliationResult
        {
            DesiredCount = prefixes.Count,
            ExistingCount = matching.Length,
            RemovedCount = matching.Length
        };
    }

    public async Task<int> CountMatchingRoutesAsync(
        IReadOnlyCollection<string> prefixes,
        IPAddress gateway,
        uint interfaceIndex,
        CancellationToken cancellationToken = default)
    {
        HashSet<string> managedPrefixes =
            prefixes.ToHashSet(
                StringComparer.OrdinalIgnoreCase);

        IReadOnlyList<SystemRoute> actual =
            await _routeManager.GetIpv4RoutesAsync(
                cancellationToken);

        return actual.Count(route =>
            managedPrefixes.Contains(route.DestinationPrefix)
            && route.InterfaceIndex == interfaceIndex
            && route.NextHop.Equals(gateway));
    }

    private static string ToIdentity(
        SystemRoute route) =>
        $"{route.DestinationPrefix}|" +
        $"{route.NextHop}|" +
        $"{route.InterfaceIndex}";
}