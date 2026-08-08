namespace PathVeer.Core.Vpn;

public sealed record VpnEndpointDefinition
{
    public required string Host { get; init; }

    public required int Port { get; init; }

    public required string Protocol { get; init; }
}