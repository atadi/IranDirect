using IranDirect.Core.Persistence;

namespace IranDirect.Core.Routing;

public sealed class RouteInventoryStore :
    JsonStore<RouteInventory>,
    IRouteInventoryPersistence
{
    private readonly SemaphoreSlim _mutex = new(1, 1);

    public RouteInventoryStore(string inventoryPath)
        : base(inventoryPath)
    {
    }

    public async Task MutateAsync(
        Func<RouteInventory, RouteInventory> transform,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transform);

        await _mutex.WaitAsync(cancellationToken);
        try
        {
            RouteInventory current = await LoadAsync(cancellationToken);
            RouteInventory updated = transform(current);
            await SaveAsync(updated, cancellationToken);
        }
        finally
        {
            _mutex.Release();
        }
    }

    public Task ClearAsync(
        CancellationToken cancellationToken = default)
    {
        return SaveAsync(
            new RouteInventory(),
            cancellationToken);
    }
}
