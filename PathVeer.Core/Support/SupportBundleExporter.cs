using System.IO.Compression;
using PathVeer.Core.Observability.Telemetry;

namespace PathVeer.Core.Support;

public sealed class SupportBundleExporter :
    ISupportBundleExporter
{
    private const string SnapshotFileName = "support-snapshot.json";

    private readonly ISupportSnapshotExporter _snapshotExporter;
    private readonly TimeProvider _timeProvider;

    public SupportBundleExporter(
        ISupportSnapshotExporter snapshotExporter,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(snapshotExporter);

        _snapshotExporter = snapshotExporter;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<SupportBundleExportResult> ExportAsync(
        string outputZipPath,
        CancellationToken cancellationToken = default)
    {
        return await ExportAsync(
            outputZipPath,
            SupportBundleExportOptions.Default,
            cancellationToken);
    }

    public async Task<SupportBundleExportResult> ExportAsync(
        string outputZipPath,
        SupportBundleExportOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            outputZipPath,
            nameof(outputZipPath));
        ArgumentNullException.ThrowIfNull(options);

        string fullZipPath = Path.GetFullPath(outputZipPath);

        if (File.Exists(fullZipPath) &&
            !options.OverwriteExisting)
        {
            throw new IOException(
                $"File '{fullZipPath}' already exists.");
        }

        string tempZipPath =
            fullZipPath + options.ZipTempSuffix;

        if (File.Exists(tempZipPath))
        {
            File.Delete(tempZipPath);
        }

        string? parentDirectory =
            Path.GetDirectoryName(fullZipPath);

        if (!string.IsNullOrWhiteSpace(parentDirectory))
        {
            Directory.CreateDirectory(parentDirectory);
        }

        string workingDirectory = CreateWorkingDirectory(
            fullZipPath);

        bool moved = false;
        SupportSnapshotExportResult? innerResult = null;

        using SupportExportTelemetry.SupportExportScope scope =
            SupportExportTelemetry.Start(
                PathVeerTagValues.OperationSupportBundleExport);

        try
        {
            string snapshotJsonPath = Path.Combine(
                workingDirectory, SnapshotFileName);

            // Explicit nested path: the snapshot exporter attaches its
            // CaptureSnapshot/Serialize/WriteJson children to this root,
            // creating no second root and no duplicate terminal metric.
            innerResult =
                await _snapshotExporter.ExportWithinBundleAsync(
                    snapshotJsonPath,
                    cancellationToken);

            using (SupportExportTelemetry.SupportChildScope zip =
                scope.StartCreateZip())
            {
                long bytesWritten = await CreateZipAsync(
                    tempZipPath,
                    snapshotJsonPath,
                    options,
                    cancellationToken);

                File.Move(
                    tempZipPath,
                    fullZipPath,
                    overwrite: true);
                moved = true;

                zip.CompleteSuccess();
                scope.CompleteSuccess();

                return new SupportBundleExportResult
                {
                    BundlePath = fullZipPath,
                    BytesWritten = bytesWritten,
                    ExportedAt = _timeProvider.GetUtcNow(),
                    Snapshot = innerResult.Snapshot
                };
            }
        }
        catch (Exception ex)
        {
            scope.CompleteFailure(ex);
            throw;
        }
        finally
        {
            try
            {
                if (Directory.Exists(workingDirectory))
                {
                    Directory.Delete(
                        workingDirectory,
                        recursive: true);
                }
            }
            catch
            {
            }

            if (!moved && File.Exists(tempZipPath))
            {
                try
                {
                    File.Delete(tempZipPath);
                }
                catch
                {
                }
            }
        }
    }

    private static string CreateWorkingDirectory(
        string fullZipPath)
    {
        string? parent = Path.GetDirectoryName(fullZipPath);
        string baseName = Path.GetFileName(fullZipPath);
        string unique =
            $"{baseName}.{Guid.NewGuid():N}.bundle";

        string root = string.IsNullOrWhiteSpace(parent)
            ? Path.GetTempPath()
            : parent;

        string fullPath = Path.Combine(root, unique);
        Directory.CreateDirectory(fullPath);
        return fullPath;
    }

    private static async Task<long> CreateZipAsync(
        string zipPath,
        string sourceFile,
        SupportBundleExportOptions options,
        CancellationToken cancellationToken)
    {
        long totalBytes;

        FileStream zipStream = new(
            zipPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            FileOptions.Asynchronous
            | FileOptions.SequentialScan
            | FileOptions.WriteThrough);

        try
        {
            using ZipArchive archive = new(
                zipStream,
                ZipArchiveMode.Create,
                leaveOpen: true);

            string entryName = options.SnapshotEntryName;

            ZipArchiveEntry entry = archive.CreateEntry(
                entryName,
                CompressionLevel.Optimal);

            await using FileStream source = new(
                sourceFile,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);

            await using Stream entryStream =
                entry.Open();

            await source.CopyToAsync(
                entryStream,
                cancellationToken);
        }
        finally
        {
            await zipStream.FlushAsync(cancellationToken);
            await zipStream.DisposeAsync();
        }

        totalBytes = new FileInfo(zipPath).Length;
        return totalBytes;
    }
}
