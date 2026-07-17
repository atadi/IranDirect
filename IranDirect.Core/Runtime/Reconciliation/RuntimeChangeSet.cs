namespace IranDirect.Core.Runtime.Reconciliation;

public sealed record RuntimeChangeSet
{
    public IReadOnlyList<RuntimeChange> Changes
        { get; init; } = [];

    public bool IsEmpty => Changes.Count == 0;

    public int Count => Changes.Count;
}