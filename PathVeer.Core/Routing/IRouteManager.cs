namespace PathVeer.Core.Routing;

public interface IRouteManager
{
    Task<IReadOnlyList<SystemRoute>> GetIpv4RoutesAsync(
        CancellationToken cancellationToken = default);

    Task AddRoutesAsync(
        IReadOnlyCollection<ManagedRoute> routes,
        CancellationToken cancellationToken = default);

    Task DeleteRoutesAsync(
        IReadOnlyCollection<ManagedRoute> routes,
        CancellationToken cancellationToken = default);
}