using System.Diagnostics;
using PathVeer.Core.Observability.Telemetry;

namespace PathVeer.Core.Support;

public sealed class SupportSnapshotExporter :
    ISupportSnapshotExporter
{
    private readonly ISupportSnapshotProvider _provider;
    private readonly ISupportSnapshotUtf8Serializer _serializer;
    private readonly TimeProvider _timeProvider;

    public SupportSnapshotExporter(
        ISupportSnapshotProvider provider,
        ISupportSnapshotUtf8Serializer serializer,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(serializer);

        _provider = provider;
        _serializer = serializer;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<SupportSnapshotExportResult> ExportAsync(
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        return await ExportAsync(
            outputPath,
            SupportSnapshotExportOptions.Default,
            cancellationToken);
    }

    public async Task<SupportSnapshotExportResult> ExportAsync(
        string outputPath,
        SupportSnapshotExportOptions options,
        CancellationToken cancellationToken = default)
    {
        return await ExportCoreAsync(
            outputPath,
            options,
            createRootTelemetry: true,
            cancellationToken);
    }

    /// <summary>
    /// Internal nested path used by <see cref="SupportBundleExporter"/>.
    /// Performs the snapshot work (capture, serialize, write) as children of
    /// the enclosing bundle export root without creating a second root Activity
    /// or recording a duplicate terminal metric. The bundle scope already owns
    /// the root and the terminal counter/histogram, so this path attaches only
    /// the CaptureSnapshot/Serialize/WriteJson children. The nested decision is
    /// explicit (the caller chooses this method) and never depends on
    /// <c>Activity.Current</c>.
    /// </summary>
    public Task<SupportSnapshotExportResult> ExportWithinBundleAsync(
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        return ExportCoreAsync(
            outputPath,
            SupportSnapshotExportOptions.Default,
            createRootTelemetry: false,
            cancellationToken);
    }

    private async Task<SupportSnapshotExportResult> ExportCoreAsync(
        string outputPath,
        SupportSnapshotExportOptions options,
        bool createRootTelemetry,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            outputPath, nameof(outputPath));
        ArgumentNullException.ThrowIfNull(options);

        // Create the telemetry root eagerly (before any throwing work) so a
        // failure during path validation still yields one root with a
        // failure outcome. The nested bundle path uses StartNested, which
        // attaches children to the enclosing root without a second root.
        SupportExportTelemetry.SupportExportScope scope =
            createRootTelemetry
                ? SupportExportTelemetry.Start(
                    PathVeerTagValues.OperationSupportSnapshotExport)
                : SupportExportTelemetry.StartNested(
                    PathVeerTagValues.OperationSupportSnapshotExport);
        using (scope)
        {
        try
        {
        string fullPath = Path.GetFullPath(outputPath);
        if (File.Exists(fullPath) &&
            !options.OverwriteExisting)
        {
            throw new IOException(
                $"File '{fullPath}' already exists.");
        }

        string? parentDirectory =
            Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(parentDirectory))
        {
            Directory.CreateDirectory(parentDirectory);
        }

            SupportSnapshot snapshot;
            using (SupportExportTelemetry.SupportChildScope capture =
                scope.StartCaptureSnapshot())
            {
                snapshot = await _provider.CaptureAsync(
                    cancellationToken);
                capture.CompleteSuccess();
            }

            byte[] bytes;
            using (SupportExportTelemetry.SupportChildScope serialize =
                scope.StartSerialize())
            {
                bytes = _serializer.SerializeToUtf8Bytes(snapshot);
                serialize.CompleteSuccess();
            }

            using (SupportExportTelemetry.SupportChildScope write =
                scope.StartWriteJson())
            {
                long bytesWritten = await WriteAtomicallyAsync(
                    outputPath,
                    bytes,
                    options,
                    cancellationToken);
                write.CompleteSuccess();

                scope.CompleteSuccess();
                return new SupportSnapshotExportResult
                {
                    OutputPath = outputPath,
                    BytesWritten = bytesWritten,
                    ExportedAt = _timeProvider.GetUtcNow(),
                    Snapshot = snapshot
                };
            }
        }
        catch (Exception ex)
        {
            scope.CompleteFailure(ex);
            throw;
        }
        }
    }

    private static async Task<long> WriteAtomicallyAsync(
        string outputPath,
        byte[] bytes,
        SupportSnapshotExportOptions options,
        CancellationToken cancellationToken)
    {
        string fullPath = Path.GetFullPath(outputPath);

        if (File.Exists(fullPath) &&
            !options.OverwriteExisting)
        {
            throw new IOException(
                $"File '{fullPath}' already exists.");
        }

        string? directory = Path.GetDirectoryName(fullPath);

        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string tempPath = fullPath + options.TempSuffix;

        if (File.Exists(tempPath))
        {
            File.Delete(tempPath);
        }

        bool tempCreated = false;
        bool moved = false;

        try
        {
            await using (FileStream stream = new(
                tempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.Asynchronous
                | FileOptions.SequentialScan
                | FileOptions.WriteThrough))
            {
                tempCreated = true;
                await stream.WriteAsync(
                    bytes, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(
                tempPath,
                fullPath,
                overwrite: true);
            moved = true;

            return bytes.LongLength;
        }
        finally
        {
            if (tempCreated && !moved)
            {
                try
                {
                    if (File.Exists(tempPath))
                    {
                        File.Delete(tempPath);
                    }
                }
                catch
                {
                }
            }
        }
    }
}
