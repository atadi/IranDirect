namespace PathVeer.Core.Runtime.Reconciliation;

public sealed class RuntimeRouteOwnershipProvider
{
    private readonly IRuntimeRouteOwnershipSource _source;

    public RuntimeRouteOwnershipProvider(
        IRuntimeRouteOwnershipSource source)
    {
        _source = source;
    }

    public async Task<RuntimeRouteOwnership> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        IReadOnlyCollection<string> endpointIdentities =
            await _source.LoadEndpointRouteIdentitiesAsync(
                cancellationToken);

        IReadOnlyCollection<string> prefixIdentities =
            await _source.LoadPrefixRouteIdentitiesAsync(
                cancellationToken);

        return new RuntimeRouteOwnership
        {
            EndpointRouteIdentities =
                Normalize(endpointIdentities),
            PrefixRouteIdentities =
                Normalize(prefixIdentities)
        };
    }

    private static IReadOnlySet<string> Normalize(
        IEnumerable<string> identities)
    {
        ArgumentNullException.ThrowIfNull(identities);

        return identities
            .Where(identity =>
                !string.IsNullOrWhiteSpace(identity))
            .Select(identity => identity.Trim())
            .ToHashSet(
                StringComparer.OrdinalIgnoreCase);
    }
}