namespace IranDirect.Core.Runtime.Reconciliation;

public sealed record RuntimeRouteOwnership
{
    public IReadOnlySet<string> EndpointRouteIdentities
        { get; init; } =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

    public IReadOnlySet<string> PrefixRouteIdentities
        { get; init; } =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
}