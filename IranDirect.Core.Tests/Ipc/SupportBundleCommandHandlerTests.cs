using IranDirect.Core.Ipc;
using IranDirect.Core.Support;

namespace IranDirect.Core.Tests.Ipc;

public sealed class SupportBundleCommandHandlerTests
{
    private static readonly DateTimeOffset FixedTime =
        new(2026, 8, 3, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ExportAsync_ForwardsPathToExporter()
    {
        RecordingExporter exporter = new();
        exporter.Result = new SupportBundleExportResult
        {
            BundlePath = "C:\\out\\bundle.zip",
            BytesWritten = 4096,
            ExportedAt = FixedTime,
            Snapshot = new SupportSnapshot
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
            }
        };

        SupportBundleCommandHandler handler =
            new(exporter);

        ServiceResponse response =
            await handler.ExportAsync(
                "C:\\out\\bundle.zip",
                CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal("C:\\out\\bundle.zip", exporter.LastPath);
        Assert.Equal(1, exporter.CallCount);
        Assert.Equal("C:\\out\\bundle.zip",
            response.SupportBundlePath);
        Assert.Equal(4096L,
            response.SupportBundleBytesWritten);
    }

    [Fact]
    public async Task ExportAsync_BlankPath_ReturnsFailure()
    {
        RecordingExporter exporter = new();
        SupportBundleCommandHandler handler =
            new(exporter);

        ServiceResponse response =
            await handler.ExportAsync(
                "   ",
                CancellationToken.None);

        Assert.False(response.Success);
        Assert.NotNull(response.ErrorCode);
        Assert.Equal(0, exporter.CallCount);
    }

    [Fact]
    public async Task ExportAsync_PropagatesExporterException()
    {
        ThrowingExporter exporter = new(
            new InvalidOperationException("write failed"));

        SupportBundleCommandHandler handler =
            new(exporter);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.ExportAsync(
                "C:\\out.zip",
                CancellationToken.None));
    }

    [Fact]
    public void Constructor_NullExporter_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => new SupportBundleCommandHandler(null!));
    }

    private sealed class RecordingExporter :
        ISupportBundleExporter
    {
        public int CallCount { get; private set; }

        public string? LastPath { get; private set; }

        public SupportBundleExportResult Result { get; set; } =
            new()
            {
                BundlePath = "",
                BytesWritten = 0,
                ExportedAt = FixedTime,
                Snapshot = new SupportSnapshot
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
                }
            };

        public Task<SupportBundleExportResult> ExportAsync(
            string outputZipPath,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastPath = outputZipPath;
            return Task.FromResult(Result);
        }

        public Task<SupportBundleExportResult> ExportAsync(
            string outputZipPath,
            SupportBundleExportOptions options,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastPath = outputZipPath;
            return Task.FromResult(Result);
        }
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
            throw _exception;
        }

        public Task<SupportBundleExportResult> ExportAsync(
            string outputZipPath,
            SupportBundleExportOptions options,
            CancellationToken cancellationToken = default)
        {
            throw _exception;
        }
    }
}
