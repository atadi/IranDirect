using System.Text.Json.Serialization;

namespace PathVeer.Core.Vpn;

public sealed record VpnEndpointInventoryItem
{
    public required string Host { get; init; }

    public required string Address { get; init; }

    public required int Port { get; init; }

    public required string Protocol { get; init; }

    public required string DestinationPrefix { get; init; }

    public required string Gateway { get; init; }

    public required uint InterfaceIndex { get; init; }

    public int Metric { get; init; } = 1;

    public bool AddedByIranDirect { get; init; }

    public bool IsCurrent { get; init; } = true;

    public DateTimeOffset ProtectedAt { get; init; }

    public DateTimeOffset LastSeenAt { get; init; }

    [JsonIgnore]
    public string Identity =>
        $"{DestinationPrefix}|{Gateway}|{InterfaceIndex}";
}