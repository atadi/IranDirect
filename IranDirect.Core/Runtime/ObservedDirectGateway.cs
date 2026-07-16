namespace IranDirect.Core.Runtime;

public sealed record ObservedDirectGateway
{
    public required string Address { get; init; }

    public required uint InterfaceIndex { get; init; }

    public required string InterfaceName { get; init; }

    public int InterfaceMetric { get; init; }
}