using System.Net;

namespace IranDirect.Core.Routing;

public sealed record ManagedRoute
{
    public required string DestinationPrefix { get; init; }

    public required IPAddress Gateway { get; init; }

    public required uint InterfaceIndex { get; init; }

    public int Metric { get; init; } = 5;

    public string Identity =>
        $"{DestinationPrefix}|{Gateway}|{InterfaceIndex}";
}