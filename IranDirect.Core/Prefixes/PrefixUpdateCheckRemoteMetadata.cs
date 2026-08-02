namespace IranDirect.Core.Prefixes;

public sealed record PrefixUpdateCheckRemoteMetadata
{
    public string? ETag { get; init; }
    public DateTimeOffset? LastModified { get; init; }
    public long? ContentLength { get; init; }
}
