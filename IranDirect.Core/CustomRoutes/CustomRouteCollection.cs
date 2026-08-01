namespace IranDirect.Core.CustomRoutes;

public sealed record CustomRouteCollection
{
    public int SchemaVersion { get; init; } = 1;

    public IReadOnlyList<CustomRouteEntry> Entries
        { get; init; } = [];
}
