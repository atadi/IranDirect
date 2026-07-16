namespace IranDirect.Core;

public sealed record IranDirectStatus
{
    public bool Enabled { get; init; }

    public string? Gateway { get; init; }

    public uint InterfaceIndex { get; init; }

    public string? InterfaceName { get; init; }

    public int PrefixCount { get; init; }

    public int InstalledRouteCount { get; init; }

    public DateTimeOffset? PrefixesUpdatedAt { get; init; }

    public string? LastError { get; init; }

    public int VpnEndpointCount { get; init; }

    public int ProtectedVpnEndpointCount { get; init; }

    public bool VpnEndpointsProtected { get; init; }
}