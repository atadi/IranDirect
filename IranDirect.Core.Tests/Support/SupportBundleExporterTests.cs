using System.IO.Compression;
using System.Text;
using IranDirect.Core.Support;

namespace IranDirect.Core.Tests.Support;

public sealed class SupportBundleExporterTests
{
    private static readonly DateTimeOffset FixedTime =
        new(2026, 8, 3, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ExportAsync_WritesValidZipArchive()
    {
        string directory = CreateTempDirectory();
        try
        {
            string zipPath = Path.Combine(
                directory, "bundle.zip");

            SupportBundleExporter exporter =
                CreateExporter(out FakeSnapshotExporter inner);

            SupportBundleExportResult result =
                await exporter.ExportAsync(zipPath);

            Assert.Equal(
                Path.GetFullPath(zipPath), result.BundlePath);

            using FileStream stream = new(
                zipPath, FileMode.Open, FileAccess.Read);
            using ZipArchive archive = new(
                stream, ZipArchiveMode.Read);

            Assert.NotEmpty(archive.Entries);
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public async Task ExportAsync_SnapshotIncludedAsFirstEntry()
    {
        string directory = CreateTempDirectory();
        try
        {
            string zipPath = Path.Combine(
                directory, "bundle.zip");

            SupportBundleExporter exporter =
                CreateExporter(out _);

            await exporter.ExportAsync(zipPath);

            using FileStream stream = new(
                zipPath, FileMode.Open, FileAccess.Read);
            using ZipArchive archive = new(
                stream, ZipArchiveMode.Read);

            Assert.Single(archive.Entries);
            Assert.Equal(
                "support-snapshot.json",
                archive.Entries[0].FullName);
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public async Task ExportAsync_Utf8JsonPreservedInsideZip()
    {
        string directory = CreateTempDirectory();
        try
        {
            string zipPath = Path.Combine(
                directory, "bundle.zip");

            SupportBundleExporter exporter =
                CreateExporter(out _);

            await exporter.ExportAsync(zipPath);

            using FileStream stream = new(
                zipPath, FileMode.Open, FileAccess.Read);
            using ZipArchive archive = new(
                stream, ZipArchiveMode.Read);

            ZipArchiveEntry entry = archive.Entries[0];

            await using Stream entryStream = entry.Open();
            using StreamReader reader = new(
                entryStream, Encoding.UTF8, true);

            string text = await reader.ReadToEndAsync();

            Assert.Contains("CapturedAt", text);
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public async Task ExportAsync_DeterministicEntryOrder()
    {
        string directory = CreateTempDirectory();
        try
        {
            string zipPath = Path.Combine(
                directory, "bundle.zip");

            SupportBundleExporter first =
                CreateExporter(out FakeSnapshotExporter a);
            SupportBundleExporter second =
                CreateExporter(out FakeSnapshotExporter b);

            a.SnapshotReturn = BuildSnapshotResult();
            b.SnapshotReturn = BuildSnapshotResult();

            await first.ExportAsync(zipPath);

            byte[] firstBytes =
                await File.ReadAllBytesAsync(zipPath);

            File.Delete(zipPath);

            await second.ExportAsync(zipPath);

            byte[] secondBytes =
                await File.ReadAllBytesAsync(zipPath);

            using FileStream fs1 = new(
                zipPath, FileMode.Open, FileAccess.Read);
            ZipArchive a1 = new(fs1, ZipArchiveMode.Read);

            Assert.Single(a1.Entries);
            Assert.Equal(
                "support-snapshot.json",
                a1.Entries[0].FullName);
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public async Task ExportAsync_TempWorkingDirectoryCleanedUp()
    {
        string directory = CreateTempDirectory();
        try
        {
            string zipPath = Path.Combine(
                directory, "bundle.zip");

            SupportBundleExporter exporter =
                CreateExporter(out _);

            await exporter.ExportAsync(zipPath);

            IEnumerable<string> leftover = Directory
                .EnumerateDirectories(directory)
                .Where(p =>
                    p.EndsWith(
                        ".bundle",
                        StringComparison.OrdinalIgnoreCase));

            Assert.Empty(leftover);
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public async Task ExportAsync_TempZipCleanedOnFailure()
    {
        string directory = CreateTempDirectory();
        try
        {
            string zipPath = Path.Combine(
                directory, "bundle.zip");

            ThrowingSnapshotExporter inner = new();
            SupportBundleExporter exporter = new(
                inner,
                new FixedTimeProvider(FixedTime));

            string tempZipPath = zipPath + ".tmp";

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => exporter.ExportAsync(zipPath));

            Assert.False(
                File.Exists(tempZipPath),
                "Temp zip must not remain on failure.");
            Assert.False(
                File.Exists(zipPath),
                "Final bundle must not exist on failure.");
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public async Task ExportAsync_OverwritesExistingBundle()
    {
        string directory = CreateTempDirectory();
        try
        {
            string zipPath = Path.Combine(
                directory, "bundle.zip");
            await File.WriteAllBytesAsync(
                zipPath, new byte[128]);

            SupportBundleExporter exporter =
                CreateExporter(out _);

            await exporter.ExportAsync(zipPath);

            using FileStream stream = new(
                zipPath, FileMode.Open, FileAccess.Read);
            using ZipArchive archive = new(
                stream, ZipArchiveMode.Read);

            Assert.Single(archive.Entries);
            Assert.NotEqual(128, stream.Length);
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public async Task ExportAsync_OverwriteDisabledRaises()
    {
        string directory = CreateTempDirectory();
        try
        {
            string zipPath = Path.Combine(
                directory, "bundle.zip");
            await File.WriteAllBytesAsync(
                zipPath, new byte[64]);

            SupportBundleExporter exporter =
                CreateExporter(out _);

            SupportBundleExportOptions options = new()
            {
                OverwriteExisting = false,
                ZipTempSuffix = ".tmp"
            };

            await Assert.ThrowsAsync<IOException>(
                () => exporter.ExportAsync(
                    zipPath,
                    options,
                    CancellationToken.None));
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public async Task ExportAsync_InnerExporterInvokedOnce()
    {
        string directory = CreateTempDirectory();
        try
        {
            string zipPath = Path.Combine(
                directory, "bundle.zip");

            FakeSnapshotExporter inner = new();
            inner.SnapshotReturn = BuildSnapshotResult();

            SupportBundleExporter exporter = new(
                inner,
                new FixedTimeProvider(FixedTime));

            await exporter.ExportAsync(zipPath);

            Assert.Equal(1, inner.CallCount);
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public async Task ExportAsync_ProviderInvokedExactlyOnce()
    {
        string directory = CreateTempDirectory();
        try
        {
            string zipPath = Path.Combine(
                directory, "bundle.zip");

            FakeSnapshotExporter inner = new();
            inner.SnapshotReturn = BuildSnapshotResult();

            SupportBundleExporter exporter = new(
                inner,
                new FixedTimeProvider(FixedTime));

            await exporter.ExportAsync(zipPath);

            Assert.Equal(1, inner.CaptureCallCount);
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public async Task ExportAsync_CancellationPropagatesFromExporter()
    {
        string directory = CreateTempDirectory();
        try
        {
            string zipPath = Path.Combine(
                directory, "bundle.zip");

            CancellingSnapshotExporter inner = new();
            SupportBundleExporter exporter = new(
                inner,
                new FixedTimeProvider(FixedTime));

            using CancellationTokenSource cts = new();
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => exporter.ExportAsync(
                    zipPath, cts.Token));
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public async Task ExportAsync_ExportedSnapshotPreserved()
    {
        string directory = CreateTempDirectory();
        try
        {
            string zipPath = Path.Combine(
                directory, "bundle.zip");

            SupportSnapshot snapshot = BuildSnapshot();

            FakeSnapshotExporter inner = new()
            {
                SnapshotReturn = new SupportSnapshotExportResult
                {
                    OutputPath = string.Empty,
                    BytesWritten = 0,
                    ExportedAt = FixedTime,
                    Snapshot = snapshot
                }
            };

            SupportBundleExporter exporter = new(
                inner,
                new FixedTimeProvider(FixedTime));

            SupportBundleExportResult result =
                await exporter.ExportAsync(zipPath);

            Assert.Same(snapshot, result.Snapshot);
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public async Task ExportAsync_BytesWrittenMatchesBundleFile()
    {
        string directory = CreateTempDirectory();
        try
        {
            string zipPath = Path.Combine(
                directory, "bundle.zip");

            SupportBundleExporter exporter =
                CreateExporter(out _);

            SupportBundleExportResult result =
                await exporter.ExportAsync(zipPath);

            FileInfo info = new(zipPath);
            Assert.Equal(info.Length, result.BytesWritten);
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public async Task ExportAsync_ExportedAtMatchesClock()
    {
        string directory = CreateTempDirectory();
        try
        {
            string zipPath = Path.Combine(
                directory, "bundle.zip");

            FakeSnapshotExporter inner = new()
            {
                SnapshotReturn = new SupportSnapshotExportResult
                {
                    OutputPath = string.Empty,
                    BytesWritten = 0,
                    ExportedAt = FixedTime,
                    Snapshot = BuildSnapshot()
                }
            };
            SupportBundleExporter exporter = new(
                inner,
                new FixedTimeProvider(FixedTime));

            SupportBundleExportResult result =
                await exporter.ExportAsync(zipPath);

            Assert.Equal(FixedTime, result.ExportedAt);
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public async Task ExportAsync_EntryUsesForwardSlashes()
    {
        string directory = CreateTempDirectory();
        try
        {
            string zipPath = Path.Combine(
                directory, "bundle.zip");

            SupportBundleExporter exporter =
                CreateExporter(out _);

            await exporter.ExportAsync(zipPath);

            using FileStream stream = new(
                zipPath, FileMode.Open, FileAccess.Read);
            using ZipArchive archive = new(
                stream, ZipArchiveMode.Read);

            ZipArchiveEntry entry = archive.Entries[0];

            Assert.False(
                entry.FullName.Contains(
                    '\\',
                    StringComparison.Ordinal),
                "Entry names must use forward slashes.");
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public async Task ExportAsync_CreatesParentDirectory()
    {
        string root = CreateTempDirectory();
        try
        {
            string zipPath = Path.Combine(
                root, "deep", "nested", "bundle.zip");

            SupportBundleExporter exporter =
                CreateExporter(out _);

            await exporter.ExportAsync(zipPath);

            Assert.True(File.Exists(zipPath));
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [Fact]
    public async Task ExportAsync_EmptyPath_Throws()
    {
        SupportBundleExporter exporter =
            CreateExporter(out _);

        await Assert.ThrowsAsync<ArgumentException>(
            () => exporter.ExportAsync(""));
    }

    [Fact]
    public async Task ExportAsync_WhitespacePath_Throws()
    {
        SupportBundleExporter exporter =
            CreateExporter(out _);

        await Assert.ThrowsAsync<ArgumentException>(
            () => exporter.ExportAsync("   "));
    }

    [Fact]
    public void Constructor_NullExporter_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => new SupportBundleExporter(null!));
    }

    private static SupportBundleExporter CreateExporter(
        out FakeSnapshotExporter inner)
    {
        inner = new FakeSnapshotExporter
        {
            SnapshotReturn = new SupportSnapshotExportResult
            {
                OutputPath = string.Empty,
                BytesWritten = 0,
                ExportedAt = FixedTime,
                Snapshot = BuildSnapshot()
            }
        };

        return new SupportBundleExporter(
            inner,
            new FixedTimeProvider(FixedTime));
    }

    private static SupportSnapshot BuildSnapshot()
    {
        return new SupportSnapshot
        {
            CapturedAt = FixedTime,
            Summary = new SupportSnapshotSummary
            {
                DiagnosticPassedCount = 1,
                DiagnosticWarningCount = 0,
                DiagnosticFailedCount = 0,
                PrefixCount = 0,
                DnsDomainCount = 0,
                ExecutionPreviewHasChanges = false,
                RuntimeAvailable = true,
                PerformanceAvailable = false
            }
        };
    }

    private static SupportSnapshotExportResult
        BuildSnapshotResult()
    {
        return new SupportSnapshotExportResult
        {
            OutputPath = string.Empty,
            BytesWritten = 0,
            ExportedAt = FixedTime,
            Snapshot = BuildSnapshot()
        };
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "IranDirect.Tests.SupportBundle",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteTempDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(
                    path,
                    recursive: true);
            }
        }
        catch
        {
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FixedTimeProvider(DateTimeOffset now)
        {
            _now = now;
        }

        public override DateTimeOffset GetUtcNow() => _now;
    }

    private sealed class FakeSnapshotExporter :
        ISupportSnapshotExporter
    {
        public int CallCount { get; private set; }

        public int CaptureCallCount
            => CallCount;

        public SupportSnapshotExportResult
            SnapshotReturn { get; set; } =
                new()
                {
                    OutputPath = "",
                    BytesWritten = 0,
                    ExportedAt = FixedTime,
                    Snapshot = BuildSnapshot()
                };

        public Task<SupportSnapshotExportResult> ExportAsync(
            string outputPath,
            CancellationToken cancellationToken = default)
        {
            return ExportAsync(
                outputPath,
                SupportSnapshotExportOptions.Default,
                cancellationToken);
        }

        public Task<SupportSnapshotExportResult> ExportAsync(
            string outputPath,
            SupportSnapshotExportOptions options,
            CancellationToken cancellationToken = default)
        {
            return WriteAndReturnAsync(
                outputPath, cancellationToken);
        }

        private async Task<SupportSnapshotExportResult>
            WriteAndReturnAsync(
                string outputPath,
                CancellationToken cancellationToken)
        {
            CallCount++;

            string? directory = Path.GetDirectoryName(
                outputPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllTextAsync(
                outputPath,
                "{\"CapturedAt\":\"2026-08-03T12:00:00+00:00\"}",
                cancellationToken);

            return SnapshotReturn;
        }
    }

    private sealed class CancellingSnapshotExporter :
        ISupportSnapshotExporter
    {
        public Task<SupportSnapshotExportResult> ExportAsync(
            string outputPath,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new InvalidOperationException(
                "Should not be reached.");
        }

        public Task<SupportSnapshotExportResult> ExportAsync(
            string outputPath,
            SupportSnapshotExportOptions options,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new InvalidOperationException(
                "Should not be reached.");
        }
    }

    private sealed class ThrowingSnapshotExporter :
        ISupportSnapshotExporter
    {
        public Task<SupportSnapshotExportResult> ExportAsync(
            string outputPath,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException(
                "Snapshot export failed.");
        }

        public Task<SupportSnapshotExportResult> ExportAsync(
            string outputPath,
            SupportSnapshotExportOptions options,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException(
                "Snapshot export failed.");
        }
    }
}
