using System.Net;

namespace PathVeer.Core.Models;

public sealed record RouteDefinition
{
    public required IPAddress NetworkAddress { get; init; }

    public required byte PrefixLength { get; init; }

    public required IPAddress Gateway { get; init; }

    public required uint InterfaceIndex { get; init; }

    public uint Metric { get; init; } = 5;

    public string DestinationPrefix =>
        $"{NetworkAddress}/{PrefixLength}";
}