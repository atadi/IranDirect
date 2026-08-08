namespace PathVeer.Core.Prefixes;

public sealed record PrefixSourceUpdateHistoryEntry
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string SourceId { get; init; } = "";
    public string SourceDisplayName { get; init; } = "";
    public string? SourceUri { get; init; }
    public string Format { get; init; } = "";
    public string ParserVersion { get; init; } = "";
    public PrefixSourceUpdateStatus Status { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset CompletedAt { get; init; }
    public TimeSpan Duration { get; init; }
    public DateTimeOffset AttemptedAt { get; init; }
    public DateTimeOffset? SourceLastModified { get; init; }
    public string? ETag { get; init; }
    public string? PreviousContentHash { get; init; }
    public string? CurrentContentHash { get; init; }
    public long? ContentLength { get; init; }
    public int PrefixCount { get; init; }
    public int AddedCount { get; init; }
    public int RemovedCount { get; init; }
    public int UnchangedCount { get; init; }
    public bool HasChanges { get; init; }
    public string? Error { get; init; }
}
