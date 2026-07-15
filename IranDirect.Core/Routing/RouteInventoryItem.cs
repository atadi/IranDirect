using System.Text.Json.Serialization;

namespace IranDirect.Core.Routing;

public sealed record RouteInventoryItem
{
    public required string DestinationPrefix { get; init; }

    public required string Gateway { get; init; }

    public required uint InterfaceIndex { get; init; }

    public int Metric { get; init; } = 5;

    [JsonIgnore]
    public string Identity =>
        $"{DestinationPrefix}|{Gateway}|{InterfaceIndex}";
}