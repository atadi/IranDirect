using System.Net;

namespace PathVeer.Core.Models;

public sealed record DirectGateway
{
    public required IPAddress Address { get; init; }

    public required uint InterfaceIndex { get; init; }

    public required string InterfaceName { get; init; }

    public required long InterfaceMetric { get; init; }
}