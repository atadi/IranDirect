namespace IranDirect.Core.Configuration;

public sealed record DesiredConfiguration
{
    public int SchemaVersion { get; init; } = 1;

    public bool Enabled { get; init; }

    public VpnProviderType VpnProvider { get; init; } =
        VpnProviderType.OpenVpn;

    public string VpnProfilePath { get; init; } =
        "vpn-profile.ovpn";

    public bool AutoRepair { get; init; } = true;

    public TimeSpan RepairInterval { get; init; } =
        TimeSpan.FromSeconds(30);

    public bool AutoUpdatePrefixes { get; init; } = true;

    public TimeSpan PrefixUpdateInterval { get; init; } =
        TimeSpan.FromDays(1);
}