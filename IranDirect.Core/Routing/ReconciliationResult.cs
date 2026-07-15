namespace IranDirect.Core.Routing;

public sealed record ReconciliationResult
{
    public int DesiredCount { get; init; }

    public int ExistingCount { get; init; }

    public int AddedCount { get; init; }

    public int RemovedCount { get; init; }

    public IReadOnlyList<string> AddedRouteIdentities
        { get; init; } = [];
}