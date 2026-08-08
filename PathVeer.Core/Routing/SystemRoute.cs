using System.Net;

namespace PathVeer.Core.Routing;

public sealed record SystemRoute
{
    public required string DestinationPrefix { get; init; }

    public required IPAddress NextHop { get; init; }

    public required uint InterfaceIndex { get; init; }

    public int RouteMetric { get; init; }
}