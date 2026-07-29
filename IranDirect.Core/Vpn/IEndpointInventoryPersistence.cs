namespace IranDirect.Core.Vpn;

public interface IEndpointInventoryPersistence
{
    Task<VpnEndpointInventory> LoadAsync(
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        VpnEndpointInventory inventory,
        CancellationToken cancellationToken = default);
}
