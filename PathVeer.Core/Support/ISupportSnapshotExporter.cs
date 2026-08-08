namespace PathVeer.Core.Support;

public interface ISupportSnapshotExporter
{
    Task<SupportSnapshotExportResult> ExportAsync(
        string outputPath,
        CancellationToken cancellationToken = default);

    Task<SupportSnapshotExportResult> ExportAsync(
        string outputPath,
        SupportSnapshotExportOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Internal nested path used by <see cref="SupportBundleExporter"/>.
    /// Performs the snapshot work as children of the enclosing bundle export
    /// root without creating a second root Activity or recording a duplicate
    /// terminal metric. The default implementation forwards to
    /// <see cref="ExportAsync(string, CancellationToken)"/>; implementers that
    /// emit their own telemetry must override this to attach to the enclosing
    /// bundle root via <c>createRootTelemetry: false</c>. The decision is
    /// explicit (the caller chooses this method) and never depends on
    /// <c>Activity.Current</c>.
    /// </summary>
    Task<SupportSnapshotExportResult> ExportWithinBundleAsync(
        string outputPath,
        CancellationToken cancellationToken = default)
        => ExportAsync(outputPath, cancellationToken);
}
