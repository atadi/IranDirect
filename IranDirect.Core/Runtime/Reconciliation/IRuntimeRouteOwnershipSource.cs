namespace IranDirect.Core.Runtime.Reconciliation;

public interface IRuntimeRouteOwnershipSource
{
    Task<IReadOnlyCollection<string>>
        LoadEndpointRouteIdentitiesAsync(
            CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<string>>
        LoadPrefixRouteIdentitiesAsync(
            CancellationToken cancellationToken = default);
}