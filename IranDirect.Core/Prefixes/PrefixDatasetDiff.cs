namespace IranDirect.Core.Prefixes;

public sealed record PrefixDatasetDiff
{
    public IReadOnlyList<string> AddedPrefixes { get; init; } = [];
    public IReadOnlyList<string> RemovedPrefixes { get; init; } = [];
    public int UnchangedCount { get; init; }
    public int AddedCount { get; init; }
    public int RemovedCount { get; init; }
    public bool HasChanges { get; init; }
}
