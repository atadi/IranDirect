namespace IranDirect.Core.Runtime.Reconciliation;

public sealed record RuntimeChange
{
    public required RuntimeChangeKind Kind { get; init; }

    public required string Identity { get; init; }

    public required string Description { get; init; }
}