namespace IranDirect.Core.Support;

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
        ArgumentException.ThrowIfNullOrWhiteSpace(
            outputPath,
            nameof(outputPath));
        ArgumentNullException.ThrowIfNull(options);

        SupportSnapshot snapshot =
            await _provider.CaptureAsync(
                cancellationToken);

        byte[] bytes = _serializer.SerializeToUtf8Bytes(snapshot);

        long bytesWritten = await WriteAtomicallyAsync(
            outputPath,
            bytes,
            options,
            cancellationToken);

        return new SupportSnapshotExportResult
        {
            OutputPath = outputPath,
            BytesWritten = bytesWritten,
            ExportedAt = _timeProvider.GetUtcNow(),
            Snapshot = snapshot
        };
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
