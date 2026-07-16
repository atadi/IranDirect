namespace IranDirect.Core.Runtime;

public sealed record ObservedRoute
{
    public required string DestinationPrefix { get; init; }

    public required string NextHop { get; init; }

    public required uint InterfaceIndex { get; init; }

    public int Metric { get; init; }

    public string Identity =>
        $"{DestinationPrefix}|{NextHop}|{InterfaceIndex}";
}