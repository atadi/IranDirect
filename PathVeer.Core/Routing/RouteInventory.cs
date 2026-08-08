namespace PathVeer.Core.Routing;

public sealed record RouteInventory
{
    public int SchemaVersion { get; init; } = 1;

    public IReadOnlyList<RouteInventoryItem> Routes
        { get; init; } = [];
}