namespace IranDirect.Core.Prefixes;

public sealed record PrefixSourceFetchResult
{
    public PrefixSourceDescriptor Source { get; init; } = new();
    public IReadOnlyList<string> Prefixes { get; init; } = [];
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset CompletedAt { get; init; }
    public TimeSpan Duration { get; init; }
    public string? ETag { get; init; }
    public DateTimeOffset? LastModified { get; init; }
    public string? ContentHash { get; init; }
    public long? ContentLength { get; init; }
    public bool NotModified { get; init; }
}
