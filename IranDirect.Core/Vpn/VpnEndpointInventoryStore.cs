using IranDirect.Core.Persistence;

namespace IranDirect.Core.Vpn;

public sealed class VpnEndpointInventoryStore :
    JsonStore<VpnEndpointInventory>
{
    public VpnEndpointInventoryStore(string inventoryPath)
        : base(inventoryPath)
    {
    }
}