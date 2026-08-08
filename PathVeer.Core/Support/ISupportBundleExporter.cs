namespace PathVeer.Core.Support;

public interface ISupportBundleExporter
{
    Task<SupportBundleExportResult> ExportAsync(
        string outputZipPath,
        CancellationToken cancellationToken = default);

    Task<SupportBundleExportResult> ExportAsync(
        string outputZipPath,
        SupportBundleExportOptions options,
        CancellationToken cancellationToken = default);
}
