namespace IranDirect.Core.Runtime;

public sealed record ObservedVpnEndpoint
{
    public required string Host { get; init; }

    public required string Address { get; init; }

    public required int Port { get; init; }

    public required string Protocol { get; init; }
}