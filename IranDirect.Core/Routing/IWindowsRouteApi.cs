namespace IranDirect.Core.Routing;

public interface IWindowsRouteApi
{
    Task<IReadOnlyList<SystemRoute>> EnumerateAsync(
        CancellationToken cancellationToken = default);

    Task AddAsync(
        IReadOnlyCollection<ManagedRoute> routes,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        IReadOnlyCollection<ManagedRoute> routes,
        CancellationToken cancellationToken = default);
}
