namespace PathVeer.Core.CustomRoutes;

public interface ICustomRouteDnsCacheRepository
{
    Task<IReadOnlyList<CustomRouteDnsCacheEntry>> GetAllAsync(
        CancellationToken cancellationToken = default);

    Task<CustomRouteDnsCacheEntry?> GetByEntryIdAsync(
        Guid customRouteEntryId,
        CancellationToken cancellationToken = default);

    Task UpsertSuccessAsync(
        Guid customRouteEntryId,
        string? domain,
        IEnumerable<string>? ipv4Addresses,
        TimeSpan cacheDuration,
        TimeSpan maxStaleDuration,
        CancellationToken cancellationToken = default);

    Task UpsertFailureAsync(
        Guid customRouteEntryId,
        string? domain,
        string? error,
        CancellationToken cancellationToken = default);

    Task<bool> RemoveAsync(
        Guid customRouteEntryId,
        CancellationToken cancellationToken = default);

    Task<int> RemoveMissingEntriesAsync(
        IReadOnlyCollection<Guid> existingEntryIds,
        CancellationToken cancellationToken = default);
}
