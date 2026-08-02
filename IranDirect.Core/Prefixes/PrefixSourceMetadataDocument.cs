namespace IranDirect.Core.Prefixes;

public sealed record PrefixSourceMetadataDocument
{
    public int SchemaVersion { get; init; } = 1;
    public PrefixSourceMetadata? Current { get; init; }
}
