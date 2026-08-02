namespace IranDirect.Core.Support;

public sealed record SupportBundleExportResult
{
    public required string BundlePath { get; init; }

    public required long BytesWritten { get; init; }

    public required DateTimeOffset ExportedAt { get; init; }

    public required SupportSnapshot Snapshot { get; init; }
}
