namespace IranDirect.Core.Prefixes;

public sealed record PrefixSourceMetadata
{
    public string SourceId { get; init; } = "";
    public string SourceDisplayName { get; init; } = "";
    public string? SourceUri { get; init; }
    public string Format { get; init; } = "";
    public string ParserVersion { get; init; } = "";
    public DateTimeOffset LastAttemptedAt { get; init; }
    public DateTimeOffset? LastSucceededAt { get; init; }
    public DateTimeOffset? SourceLastModified { get; init; }
    public string? ETag { get; init; }
    public string? ContentHash { get; init; }
    public long? ContentLength { get; init; }
    public int PrefixCount { get; init; }
    public TimeSpan? DownloadDuration { get; init; }
    public PrefixSourceUpdateStatus LastStatus { get; init; }
    public string? LastError { get; init; }
}
