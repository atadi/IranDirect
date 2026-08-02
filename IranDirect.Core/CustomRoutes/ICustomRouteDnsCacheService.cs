namespace IranDirect.Core.CustomRoutes;

public interface ICustomRouteDnsCacheService
{
    Task<IReadOnlyList<CustomRouteDnsCacheStatus>> GetStatusAsync(
        CancellationToken cancellationToken = default);
}
