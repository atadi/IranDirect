namespace PathVeer.Core.Vpn;

public sealed record VpnEndpointInventory
{
    public int SchemaVersion { get; init; } = 1;

    public IReadOnlyList<VpnEndpointInventoryItem> Endpoints
        { get; init; } = [];
}