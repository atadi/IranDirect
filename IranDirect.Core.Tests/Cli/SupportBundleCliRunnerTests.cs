using IranDirect.Cli;
using IranDirect.Core.Support;

namespace IranDirect.Core.Tests.Cli;

public sealed class SupportBundleCliRunnerTests
{
    private static readonly DateTimeOffset FixedTime =
        new(2026, 8, 3, 14, 22, 1, TimeSpan.Zero);

    [Fact]
    public async Task RunAsync_NoArgs_UsesDefaultPath()
    {
        RecordingExporter exporter = new();
        exporter.Result = BuildResult("C:\\tmp\\default.zip");

        string tempRoot = Path.Combine(
            Path.GetTempPath(),
            "IranDirect.Tests.SupportBundleCli",
            Guid.NewGuid().ToString("N"));

        try
        {
            (int exitCode, string stdout, _) =
                await RunAsync(
                    [],
                    exporter,
                    timeProvider: new FixedTimeProvider(FixedTime),
                    tempRootOverride: tempRoot);

            Assert.Equal(0, exitCode);
            Assert.Equal(1, exporter.CallCount);
            Assert.StartsWith(
                Path.Combine(
                    tempRoot,
                    "IranDirect",
                    "SupportBundles"),
                exporter.LastOutputPath!);
            Assert.Contains(
                "IranDirect-Support-",
                Path.GetFileName(exporter.LastOutputPath!));
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public async Task RunAsync_CustomPath_UsesUserPath()
    {
        RecordingExporter exporter = new();
        exporter.Result = BuildResult("");

        string file = Path.Combine(
            Path.GetTempPath(),
            "custom-bundle.zip");

        try
        {
            (int exitCode, _, _) =
                await RunAsync(
                    [file],
                    exporter);

            Assert.Equal(0, exitCode);
            Assert.Equal(file, exporter.LastOutputPath);
        }
        finally
        {
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
    }

    [Fact]
    public async Task RunAsync_ExporterInvokedOnce()
    {
        RecordingExporter exporter = new();
        exporter.Result = BuildResult("C:\\tmp\\once.zip");

        await RunAsync(
            [Path.Combine(Path.GetTempPath(), "once.zip")],
            exporter);

        Assert.Equal(1, exporter.CallCount);
    }

    [Fact]
    public async Task RunAsync_Success_RendersExpectedSections()
    {
        RecordingExporter exporter = new();
        SupportSnapshot summary = BuildSnapshot(
            warnings: 1,
            failures: 0,
            hasChanges: true,
            prefixCount: 1946,
            dnsDomainCount: 8);
        exporter.Result = new SupportBundleExportResult
        {
            BundlePath = "C:\\bundles\\site.zip",
            BytesWritten = 12345,
            ExportedAt = FixedTime,
            Snapshot = summary
        };

        string file = Path.Combine(
            Path.GetTempPath(),
            Guid.NewGuid().ToString("N") + ".zip");

        (int exitCode, string stdout, _) =
            await RunAsync(
                [file],
                exporter);

        Assert.Equal(0, exitCode);
        Assert.Contains("=== Support Bundle ===", stdout);
        Assert.Contains("Bundle:", stdout);
        Assert.Contains(
            "C:\\bundles\\site.zip", stdout);
        Assert.Contains("Size:", stdout);
        Assert.Contains("12345 bytes", stdout);
        Assert.Contains("Created:", stdout);
        Assert.Contains(
            "2026-08-03 14:22:01 UTC",
            stdout);
        Assert.Contains("Summary:", stdout);
        Assert.Contains("Warnings: 1", stdout);
        Assert.Contains("Failures: 0", stdout);
        Assert.Contains(
            "Preview Changes: True",
            stdout);
        Assert.Contains("Prefixes: 1946", stdout);
        Assert.Contains("DNS Domains: 8", stdout);
    }

    [Fact]
    public async Task RunAsync_ExporterThrows_ReturnsExitCode1()
    {
        ThrowingExporter exporter = new(
            new InvalidOperationException("disk full"));

        string file = Path.Combine(
            Path.GetTempPath(),
            Guid.NewGuid().ToString("N") + ".zip");

        (int exitCode, _, string stderr) =
            await RunAsync(
                [file],
                exporter);

        Assert.Equal(1, exitCode);
        Assert.Contains(
            "Support bundle export failed: disk full",
            stderr);
    }

    [Fact]
    public async Task RunAsync_Timeout_ReturnsExitCode3()
    {
        ThrowingExporter exporter = new(
            new TimeoutException("snap timed out"));

        string file = Path.Combine(
            Path.GetTempPath(),
            Guid.NewGuid().ToString("N") + ".zip");

        (int exitCode, _, string stderr) =
            await RunAsync(
                [file],
                exporter);

        Assert.Equal(3, exitCode);
        Assert.Contains(
            "snap timed out", stderr);
    }

    [Fact]
    public async Task RunAsync_Cancellation_ReturnsExitCode4()
    {
        ThrowingExporter exporter = new(
            new OperationCanceledException());

        string file = Path.Combine(
            Path.GetTempPath(),
            Guid.NewGuid().ToString("N") + ".zip");

        (int exitCode, _, string stderr) =
            await RunAsync(
                [file],
                exporter);

        Assert.Equal(4, exitCode);
        Assert.Contains(
            "canceled", stderr);
    }

    [Fact]
    public async Task RunAsync_TooManyArgs_ReturnsExitCode6()
    {
        RecordingExporter exporter = new();
        exporter.Result = BuildResult("");

        (int exitCode, _, string stderr) =
            await RunAsync(
                ["one", "two"],
                exporter);

        Assert.Equal(6, exitCode);
        Assert.Contains(
            "Usage:", stderr);
        Assert.Equal(0, exporter.CallCount);
    }

    [Fact]
    public async Task RunAsync_EmptyPathArg_ReturnsExitCode6()
    {
        RecordingExporter exporter = new();
        exporter.Result = BuildResult("");

        (int exitCode, _, string stderr) =
            await RunAsync(
                [""],
                exporter);

        Assert.Equal(6, exitCode);
        Assert.Contains(
            "Usage:", stderr);
        Assert.Equal(0, exporter.CallCount);
    }

    [Fact]
    public async Task RunAsync_NullExporter_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => SupportBundleCliRunner.RunAsync(
                ["x.zip"],
                exporter: null!,
                stdout: new StringWriter(),
                stderr: new StringWriter()));
    }

    [Fact]
    public void DefaultPathBuilder_ProducesFilenameWithTimestamp()
    {
        string tempRoot = Path.Combine(
            Path.GetTempPath(),
            "IranDirect.Tests.SupportBundleCli",
            Guid.NewGuid().ToString("N"));

        try
        {
            string path =
                IranDirect.Core.Cli.SupportBundlePathBuilder
                    .CreateDefaultPath(
                        new FixedTimeProvider(FixedTime),
                        tempRoot);

            string filename = Path.GetFileName(path);

            Assert.StartsWith(
                "IranDirect-Support-", filename);
            Assert.EndsWith(".zip", filename);
            Assert.Contains("20260803", filename);
            Assert.Matches(
                @"IranDirect-Support-\d{8}-\d{6}\.zip",
                filename);
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void Runner_DoesNotReferenceSnapshotProviderOrSerializerOrExporter()
    {
        System.Reflection.Assembly runner =
            typeof(SupportBundleCliRunner).Assembly;

        string[] forbiddenNames =
        [
            "SupportSnapshotProvider",
            "SupportSnapshotSerializer",
            "SupportSnapshotExporter",
            "SupportBundleExporter"
        ];

        foreach (string forbidden in forbiddenNames)
        {
            System.Type? referenced = runner.GetType(
                $"IranDirect.Cli.{forbidden}",
                throwOnError: false);

            Assert.Null(referenced);
        }
    }

    private static async Task<
        (int ExitCode, string Stdout, string Stderr)>
        RunAsync(
            string[] args,
            ISupportBundleExporter exporter,
            TimeProvider? timeProvider = null,
            string? tempRootOverride = null)
    {
        using StringWriter stdout = new();
        using StringWriter stderr = new();

        int exitCode = await SupportBundleCliRunner.RunAsync(
            args,
            exporter,
            stdout,
            stderr,
            timeProvider,
            tempRootOverride);

        return (exitCode, stdout.ToString(), stderr.ToString());
    }

    private static SupportBundleExportResult BuildResult(
        string bundlePath)
    {
        return new SupportBundleExportResult
        {
            BundlePath = bundlePath,
            BytesWritten = 0,
            ExportedAt = FixedTime,
            Snapshot = BuildSnapshot()
        };
    }

    private static SupportSnapshot BuildSnapshot(
        int warnings = 0,
        int failures = 0,
        bool hasChanges = false,
        int prefixCount = 0,
        int dnsDomainCount = 0)
    {
        return new SupportSnapshot
        {
            CapturedAt = FixedTime,
            Summary = new SupportSnapshotSummary
            {
                DiagnosticPassedCount = 0,
                DiagnosticWarningCount = warnings,
                DiagnosticFailedCount = failures,
                PrefixCount = prefixCount,
                DnsDomainCount = dnsDomainCount,
                ExecutionPreviewHasChanges = hasChanges,
                RuntimeAvailable = true,
                PerformanceAvailable = false
            }
        };
    }

    private static void DeleteDirectory(string path)
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

    private sealed class RecordingExporter :
        ISupportBundleExporter
    {
        public int CallCount { get; private set; }

        public string? LastOutputPath { get; private set; }

        public SupportBundleExportResult Result { get; set; } =
            new()
            {
                BundlePath = "",
                BytesWritten = 0,
                ExportedAt = DateTimeOffset.UtcNow,
                Snapshot = BuildDefault()
            };

        public Task<SupportBundleExportResult> ExportAsync(
            string outputZipPath,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastOutputPath = outputZipPath;
            return Task.FromResult(Result);
        }

        public Task<SupportBundleExportResult> ExportAsync(
            string outputZipPath,
            SupportBundleExportOptions options,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastOutputPath = outputZipPath;
            return Task.FromResult(Result);
        }

        private static SupportSnapshot BuildDefault() =>
            new()
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
    }

    private sealed class ThrowingExporter :
        ISupportBundleExporter
    {
        private readonly Exception _exception;

        public ThrowingExporter(Exception exception)
        {
            _exception = exception;
        }

        public Task<SupportBundleExportResult> ExportAsync(
            string outputZipPath,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw _exception;
        }

        public Task<SupportBundleExportResult> ExportAsync(
            string outputZipPath,
            SupportBundleExportOptions options,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw _exception;
        }
    }
}
