using PathVeer.Core.Routing;
using PathVeer.Core.Vpn;

namespace PathVeer.Core.Runtime.Reconciliation;

public sealed class InventoryRouteOwnershipSource :
    IRuntimeRouteOwnershipSource
{
    private readonly RouteInventoryStore _routeInventoryStore;
    private readonly VpnEndpointInventoryStore _vpnEndpointInventoryStore;

    public InventoryRouteOwnershipSource(
        RouteInventoryStore routeInventoryStore,
        VpnEndpointInventoryStore vpnEndpointInventoryStore)
    {
        _routeInventoryStore = routeInventoryStore;
        _vpnEndpointInventoryStore = vpnEndpointInventoryStore;
    }

    public async Task<IReadOnlyCollection<string>>
        LoadEndpointRouteIdentitiesAsync(
            CancellationToken cancellationToken = default)
    {
        VpnEndpointInventory inventory =
            await _vpnEndpointInventoryStore.LoadAsync(
                cancellationToken);

        return inventory.Endpoints
            .Where(item => item.AddedByIranDirect)
            .Select(item => item.Identity)
            .ToArray();
    }

    public async Task<IReadOnlyCollection<string>>
        LoadPrefixRouteIdentitiesAsync(
            CancellationToken cancellationToken = default)
    {
        RouteInventory inventory =
            await _routeInventoryStore.LoadAsync(
                cancellationToken);

        return inventory.Routes
            .Select(item => item.Identity)
            .ToArray();
    }
}
