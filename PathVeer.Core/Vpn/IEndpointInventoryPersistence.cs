namespace PathVeer.Core.Vpn;

public interface IEndpointInventoryPersistence
{
    Task<VpnEndpointInventory> LoadAsync(
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        VpnEndpointInventory inventory,
        CancellationToken cancellationToken = default);

    Task MutateAsync(
        Func<VpnEndpointInventory, VpnEndpointInventory> transform,
        CancellationToken cancellationToken = default);
}
