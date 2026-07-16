namespace IranDirect.Core.Runtime;

public sealed record ObservedRuntime
{
    public bool VpnProfileExists { get; init; }

    public bool VpnProfileValid { get; init; }

    public ObservedDirectGateway? DirectGateway { get; init; }

    public IReadOnlyList<ObservedVpnEndpoint> VpnEndpoints
        { get; init; } = [];

    public IReadOnlyList<string> Prefixes
        { get; init; } = [];

    public IReadOnlyList<ObservedRoute> Routes
        { get; init; } = [];

    public DateTimeOffset ObservedAt { get; init; }
}