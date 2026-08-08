namespace PathVeer.Core.Prefixes;

public sealed record PrefixSourceChangeSummary
{
    public string? PreviousContentHash { get; init; }
    public string? CurrentContentHash { get; init; }
    public int AddedCount { get; init; }
    public int RemovedCount { get; init; }
    public int UnchangedCount { get; init; }
    public bool HasChanges { get; init; }
    public DateTimeOffset ComparedAt { get; init; }
}
