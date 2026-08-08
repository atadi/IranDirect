namespace PathVeer.Core.Runtime;

public sealed record DesiredPrefixRoute
{
    public required string DestinationPrefix { get; init; }

    public required string Gateway { get; init; }

    public required uint InterfaceIndex { get; init; }

    public int Metric { get; init; } = 5;

    public string Identity =>
        $"{DestinationPrefix}|{Gateway}|{InterfaceIndex}";
}