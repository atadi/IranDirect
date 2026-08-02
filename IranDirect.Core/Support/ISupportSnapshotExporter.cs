namespace IranDirect.Core.Support;

public interface ISupportSnapshotExporter
{
    Task<SupportSnapshotExportResult> ExportAsync(
        string outputPath,
        CancellationToken cancellationToken = default);

    Task<SupportSnapshotExportResult> ExportAsync(
        string outputPath,
        SupportSnapshotExportOptions options,
        CancellationToken cancellationToken = default);
}
