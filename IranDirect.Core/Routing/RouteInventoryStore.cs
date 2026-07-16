using IranDirect.Core.Persistence;

namespace IranDirect.Core.Routing;

public sealed class RouteInventoryStore :
    JsonStore<RouteInventory>
{
    public RouteInventoryStore(string inventoryPath)
        : base(inventoryPath)
    {
    }

    public Task ClearAsync(
        CancellationToken cancellationToken = default)
    {
        return SaveAsync(
            new RouteInventory(),
            cancellationToken);
    }
}