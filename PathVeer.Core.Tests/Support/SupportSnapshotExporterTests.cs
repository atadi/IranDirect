using System.Text;
using PathVeer.Core.Configuration;
using PathVeer.Core.CustomRoutes;
using PathVeer.Core.Diagnostics;
using PathVeer.Core.Observability;
using PathVeer.Core.Planning;
using PathVeer.Core.Prefixes;
using PathVeer.Core.Runtime.Profiling;
using PathVeer.Core.Support;

namespace PathVeer.Core.Tests.Support;

public sealed class SupportSnapshotExporterTests
{
    private static readonly DateTimeOffset FixedTime =
        new(2026, 8, 3, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ExportAsync_WritesFileAtomically()
    {
        string directory = CreateTempDirectory();
        try
        {
            string path = Path.Combine(
                directory, "snapshot.json");

            SupportSnapshotExporter exporter =
                CreateExporter(out FakeProvider provider);

            SupportSnapshotExportResult result =
                await exporter.ExportAsync(path);

            Assert.True(File.Exists(path));
            Assert.False(
                File.Exists(path + ".tmp"),
                "Temp file must not remain.");
            Assert.Equal(
                Path.GetFullPath(path),
                result.OutputPath);
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public async Task ExportAsync_CreatesMissingDirectories()
    {
        string root = CreateTempDirectory();
        try
        {
            string nestedPath = Path.Combine(
                root, "a", "b", "c", "snapshot.json");

            SupportSnapshotExporter exporter =
                CreateExporter(out _);

            await exporter.ExportAsync(nestedPath);

            Assert.True(File.Exists(nestedPath));
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [Fact]
    public async Task ExportAsync_OverwritesExistingFile()
    {
        string directory = CreateTempDirectory();
        try
        {
            string path = Path.Combine(
                directory, "snapshot.json");
            await File.WriteAllTextAsync(
                path, "previous-content");

            SupportSnapshotExporter exporter =
                CreateExporter(out _);

            await exporter.ExportAsync(path);

            string contents =
                await File.ReadAllTextAsync(path);
            Assert.NotEqual(
                "previous-content", contents);
            Assert.Contains(
                "CapturedAt", contents);
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public async Task ExportAsync_DoesNotOverwriteWhenDisabled()
    {
        string directory = CreateTempDirectory();
        try
        {
            string path = Path.Combine(
                directory, "snapshot.json");
            await File.WriteAllTextAsync(
                path, "previous-content");

            SupportSnapshotExporter exporter =
                CreateExporter(out _);

            SupportSnapshotExportOptions options = new()
            {
                OverwriteExisting = false,
                TempSuffix = ".tmp"
            };

            await Assert.ThrowsAsync<IOException>(
                () => exporter.ExportAsync(
                    path,
                    options,
                    CancellationToken.None));
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public async Task ExportAsync_WritesUtf8WithoutBom()
    {
        string directory = CreateTempDirectory();
        try
        {
            string path = Path.Combine(
                directory, "snapshot.json");

            SupportSnapshotExporter exporter =
                CreateExporter(out _);

            await exporter.ExportAsync(path);

            byte[] bytes = await File.ReadAllBytesAsync(path);

            int expectedBomStart = 0;

            if (bytes.Length >= 3
                && bytes[0] == 0xEF
                && bytes[1] == 0xBB
                && bytes[2] == 0xBF)
            {
                expectedBomStart = 3;
                Assert.Fail(
                    "Output must not include a UTF-8 BOM.");
            }

            string text = Encoding.UTF8.GetString(
                bytes, expectedBomStart,
                bytes.Length - expectedBomStart);
            Assert.Contains("CapturedAt", text);
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public async Task ExportAsync_TempFileCleanedAfterFailure()
    {
        string directory = CreateTempDirectory();
        try
        {
            string path = Path.Combine(
                directory, "snapshot.json");

            ThrowingAfterSerializeProvider provider =
                new();
            SupportSnapshotSerializer serializer =
                new();
            SupportSnapshotExporter exporter = new(
                provider,
                serializer);

            string beforeTempPath = path + ".tmp";

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => exporter.ExportAsync(path));

            Assert.False(
                File.Exists(beforeTempPath),
                "Temp file must be removed on failure.");

            Assert.False(
                File.Exists(path),
                "Target file must not exist on failure.");
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public async Task ExportAsync_TempFileCleanedAfterCancellation()
    {
        string directory = CreateTempDirectory();
        try
        {
            string path = Path.Combine(
                directory, "snapshot.json");

            CancellingProvider provider = new();

            SupportSnapshotExporter exporter = new(
                provider,
                new SupportSnapshotSerializer());

            string beforeTempPath = path + ".tmp";

            using CancellationTokenSource cts = new();
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => exporter.ExportAsync(
                    path, cts.Token));

            Assert.False(
                File.Exists(beforeTempPath));
            Assert.False(File.Exists(path));
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public async Task ExportAsync_DestinationReplacedAtomically()
    {
        string directory = CreateTempDirectory();
        try
        {
            string path = Path.Combine(
                directory, "snapshot.json");

            await File.WriteAllTextAsync(
                path,
                "{\n  \"Version\": 1\n}\n");

            SupportSnapshotExporter exporter =
                CreateExporter(out _);

            await exporter.ExportAsync(path);

            byte[] contentBytes =
                await File.ReadAllBytesAsync(path);
            string content =
                Encoding.UTF8.GetString(contentBytes);

            Assert.DoesNotContain(
                "\"Version\": 1", content);
            Assert.Contains(
                "CapturedAt", content);
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public async Task ExportAsync_SerializerInvokedOnce()
    {
        string directory = CreateTempDirectory();
        try
        {
            string path = Path.Combine(
                directory, "snapshot.json");

            CountingSerializer serializer =
                new(new SupportSnapshotSerializer());
            FakeProvider provider = new();

            SupportSnapshotExporter exporter = new(
                provider,
                serializer);

            await exporter.ExportAsync(path);

            Assert.Equal(1, serializer.CallCount);
            Assert.Equal(1, provider.CallCount);
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public async Task ExportAsync_ProviderInvokedOnce()
    {
        string directory = CreateTempDirectory();
        try
        {
            string path = Path.Combine(
                directory, "snapshot.json");

            FakeProvider provider = new();

            SupportSnapshotExporter exporter = new(
                provider,
                new SupportSnapshotSerializer());

            await exporter.ExportAsync(path);

            Assert.Equal(1, provider.CallCount);
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public async Task ExportAsync_CancellationPropagatesFromProvider()
    {
        string directory = CreateTempDirectory();
        try
        {
            string path = Path.Combine(
                directory, "snapshot.json");

            CancellingProvider provider = new();
            SupportSnapshotExporter exporter = new(
                provider,
                new SupportSnapshotSerializer());

            using CancellationTokenSource cts = new();
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => exporter.ExportAsync(
                    path, cts.Token));
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public async Task ExportAsync_EmptyPath_Throws()
    {
        SupportSnapshotExporter exporter =
            CreateExporter(out _);

        await Assert.ThrowsAsync<ArgumentException>(
            () => exporter.ExportAsync(""));
    }

    [Fact]
    public async Task ExportAsync_WhitespacePath_Throws()
    {
        SupportSnapshotExporter exporter =
            CreateExporter(out _);

        await Assert.ThrowsAsync<ArgumentException>(
            () => exporter.ExportAsync("   "));
    }

    [Fact]
    public async Task ExportAsync_NullOptions_Throws()
    {
        string directory = CreateTempDirectory();
        try
        {
            string path = Path.Combine(
                directory, "snapshot.json");

            SupportSnapshotExporter exporter =
                CreateExporter(out _);

            await Assert.ThrowsAsync<ArgumentNullException>(
                () => exporter.ExportAsync(
                    path,
                    options: null!,
                    CancellationToken.None));
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public async Task ExportAsync_BytesWrittenMatchesFileSize()
    {
        string directory = CreateTempDirectory();
        try
        {
            string path = Path.Combine(
                directory, "snapshot.json");

            SupportSnapshotExporter exporter =
                CreateExporter(out _);

            SupportSnapshotExportResult result =
                await exporter.ExportAsync(path);

            FileInfo info = new(path);
            Assert.Equal(info.Length, result.BytesWritten);
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
            string path = Path.Combine(
                directory, "snapshot.json");

            SupportSnapshotExporter exporter =
                CreateExporter(out FakeProvider provider);

            SupportSnapshotExportResult result =
                await exporter.ExportAsync(path);

            Assert.Same(provider.Snapshot, result.Snapshot);

            string content =
                await File.ReadAllTextAsync(path);

            using System.Text.Json.JsonDocument doc =
                System.Text.Json.JsonDocument.Parse(content);

            Assert.Equal(
                FixedTime,
                doc.RootElement
                    .GetProperty("CapturedAt")
                    .GetDateTimeOffset());
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
            string path = Path.Combine(
                directory, "snapshot.json");

            FixedTimeProvider time = new(FixedTime);
            FakeProvider provider = new();
            SupportSnapshotExporter exporter = new(
                provider,
                new SupportSnapshotSerializer(),
                time);

            SupportSnapshotExportResult result =
                await exporter.ExportAsync(path);

            Assert.Equal(
                FixedTime,
                result.ExportedAt);
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public void Constructor_NullProvider_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => new SupportSnapshotExporter(
                null!,
                new SupportSnapshotSerializer()));
    }

    [Fact]
    public void Constructor_NullSerializer_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => new SupportSnapshotExporter(
                new FakeProvider(),
                null!));
    }

    private static SupportSnapshotExporter CreateExporter(
        out FakeProvider provider)
    {
        provider = new();
        return new SupportSnapshotExporter(
            provider,
            new SupportSnapshotSerializer(),
            new FixedTimeProvider(FixedTime));
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "PathVeer.Tests.SupportExport",
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

    private sealed class FakeProvider :
        ISupportSnapshotProvider
    {
        public int CallCount { get; private set; }

        public SupportSnapshot Snapshot { get; } = new()
        {
            CapturedAt = FixedTime,
            Summary = new SupportSnapshotSummary
            {
                DiagnosticPassedCount = 0,
                DiagnosticWarningCount = 0,
                DiagnosticFailedCount = 0,
                PrefixCount = 0,
                DnsDomainCount = 0,
                ExecutionPreviewHasChanges = false,
                RuntimeAvailable = true,
                PerformanceAvailable = false
            }
        };

        public Task<SupportSnapshot> CaptureAsync(
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(Snapshot);
        }
    }

    private sealed class CancellingProvider :
        ISupportSnapshotProvider
    {
        public Task<SupportSnapshot> CaptureAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new InvalidOperationException(
                "Should not be reached.");
        }
    }

    private sealed class ThrowingAfterSerializeProvider :
        ISupportSnapshotProvider
    {
        public Task<SupportSnapshot> CaptureAsync(
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException(
                "Capture failed.");
        }
    }

    private sealed class CountingSerializer :
        ISupportSnapshotSerializer,
        ISupportSnapshotUtf8Serializer
    {
        private readonly SupportSnapshotSerializer _inner;

        public CountingSerializer(
            SupportSnapshotSerializer inner)
        {
            _inner = inner;
        }

        public int CallCount { get; private set; }

        public string Serialize(SupportSnapshot snapshot)
        {
            CallCount++;
            return _inner.Serialize(snapshot);
        }

        public byte[] SerializeToUtf8Bytes(SupportSnapshot snapshot)
        {
            CallCount++;
            return _inner.SerializeToUtf8Bytes(snapshot);
        }
    }
}
