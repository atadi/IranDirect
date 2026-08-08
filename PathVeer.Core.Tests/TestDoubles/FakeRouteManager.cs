using PathVeer.Core.Routing;

namespace PathVeer.Core.Tests.TestDoubles;

internal sealed class FakeRouteManager : IRouteManager
{
    private readonly List<SystemRoute> _routes = [];

    public IReadOnlyList<ManagedRoute> AddedRoutes
        { get; private set; } = [];

    public IReadOnlyList<ManagedRoute> DeletedRoutes
        { get; private set; } = [];

    public FakeRouteManager(
        IEnumerable<SystemRoute>? initialRoutes = null)
    {
        if (initialRoutes is not null)
        {
            _routes.AddRange(initialRoutes);
        }
    }

    public Task<IReadOnlyList<SystemRoute>> GetIpv4RoutesAsync(
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<SystemRoute>>(
            _routes.ToArray());
    }

    public Task AddRoutesAsync(
        IReadOnlyCollection<ManagedRoute> routes,
        CancellationToken cancellationToken = default)
    {
        AddedRoutes = routes.ToArray();

        foreach (ManagedRoute route in routes)
        {
            _routes.Add(
                new SystemRoute
                {
                    DestinationPrefix =
                        route.DestinationPrefix,
                    NextHop = route.Gateway,
                    InterfaceIndex =
                        route.InterfaceIndex,
                    RouteMetric = route.Metric
                });
        }

        return Task.CompletedTask;
    }

    public Task DeleteRoutesAsync(
        IReadOnlyCollection<ManagedRoute> routes,
        CancellationToken cancellationToken = default)
    {
        DeletedRoutes = routes.ToArray();

        foreach (ManagedRoute route in routes)
        {
            _routes.RemoveAll(
                existing =>
                    existing.DestinationPrefix.Equals(
                        route.DestinationPrefix,
                        StringComparison.OrdinalIgnoreCase) &&
                    existing.NextHop.Equals(route.Gateway) &&
                    existing.InterfaceIndex ==
                    route.InterfaceIndex);
        }

        return Task.CompletedTask;
    }
}