namespace PathVeer.Core.Support;

public sealed record SupportSnapshotExportResult
{
    public required string OutputPath { get; init; }

    public required long BytesWritten { get; init; }

    public required DateTimeOffset ExportedAt { get; init; }

    public required SupportSnapshot Snapshot { get; init; }
}
