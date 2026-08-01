namespace IranDirect.Core.CustomRoutes;

public sealed record CustomRouteDnsCacheCollection
{
    public int SchemaVersion { get; init; } = 1;

    public IReadOnlyList<CustomRouteDnsCacheEntry> Entries { get; init; } = [];
}
