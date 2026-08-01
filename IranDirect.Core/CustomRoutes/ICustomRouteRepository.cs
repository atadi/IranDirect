namespace IranDirect.Core.CustomRoutes;

public interface ICustomRouteRepository
{
    Task<IReadOnlyList<CustomRouteEntry>> GetAllAsync(
        CancellationToken cancellationToken = default);

    Task MutateAsync(
        Func<CustomRouteCollection, CustomRouteCollection> transform,
        CancellationToken cancellationToken = default);
}
