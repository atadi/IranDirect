namespace PathVeer.Core.Runtime.Reconciliation;

public sealed record RuntimeChange
{
    public required RuntimeChangeKind Kind { get; init; }

    public required string Identity { get; init; }

    public required string DestinationPrefix { get; init; }

    public required string Gateway { get; init; }

    public required uint InterfaceIndex { get; init; }

    public required int Metric { get; init; }

    public required string Description { get; init; }
}