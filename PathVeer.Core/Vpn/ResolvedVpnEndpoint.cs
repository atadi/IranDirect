namespace PathVeer.Core.Vpn;

public sealed record ResolvedVpnEndpoint
{
    public required string Host { get; init; }

    public required string Address { get; init; }

    public required int Port { get; init; }

    public required string Protocol { get; init; }
}