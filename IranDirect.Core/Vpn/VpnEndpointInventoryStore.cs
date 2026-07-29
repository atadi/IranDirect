using IranDirect.Core.Persistence;

namespace IranDirect.Core.Vpn;

public sealed class VpnEndpointInventoryStore :
    JsonStore<VpnEndpointInventory>,
    IEndpointInventoryPersistence
{
    private readonly SemaphoreSlim _mutex = new(1, 1);

    public VpnEndpointInventoryStore(string inventoryPath)
        : base(inventoryPath)
    {
    }

    public async Task MutateAsync(
        Func<VpnEndpointInventory, VpnEndpointInventory> transform,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transform);

        await _mutex.WaitAsync(cancellationToken);
        try
        {
            VpnEndpointInventory current = await LoadAsync(cancellationToken);
            VpnEndpointInventory updated = transform(current);
            await SaveAsync(updated, cancellationToken);
        }
        finally
        {
            _mutex.Release();
        }
    }
}
