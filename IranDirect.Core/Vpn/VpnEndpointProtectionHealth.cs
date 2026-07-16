namespace IranDirect.Core.Vpn;

public sealed record VpnEndpointProtectionHealth
{
    public int CurrentEndpointCount { get; init; }

    public int ProtectedEndpointCount { get; init; }

    public bool IsProtected =>
        CurrentEndpointCount > 0 &&
        CurrentEndpointCount == ProtectedEndpointCount;
}