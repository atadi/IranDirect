using PathVeer.Cli;
using PathVeer.Core.Support;

namespace PathVeer.Core.Tests.Cli;

public sealed class SupportBundleCliRendererTests
{
    [Fact]
    public void Render_IncludesAllExpectedSections()
    {
        SupportBundleExportResult result = new()
        {
            BundlePath = "C:\\bundles\\site.zip",
            BytesWritten = 12345,
            ExportedAt = new DateTimeOffset(
                2026, 8, 3, 14, 22, 1, TimeSpan.Zero),
            Snapshot = new SupportSnapshot
            {
                CapturedAt = new DateTimeOffset(
                    2026, 8, 3, 14, 22, 1, TimeSpan.Zero),
                Summary = new SupportSnapshotSummary
                {
                    DiagnosticPassedCount = 0,
                    DiagnosticWarningCount = 1,
                    DiagnosticFailedCount = 0,
                    PrefixCount = 1946,
                    DnsDomainCount = 8,
                    ExecutionPreviewHasChanges = true,
                    RuntimeAvailable = true,
                    PerformanceAvailable = false
                }
            }
        };

        List<string> lines = [
            ..PathVeer.Core.Cli.SupportBundleCliRenderer
                .Render(result)
        ];

        Assert.Contains(
            "=== Support Bundle ===", lines);
        Assert.Contains(
            "Bundle:", lines);
        Assert.Contains(
            "C:\\bundles\\site.zip", lines);
        Assert.Contains("Size:", lines);
        Assert.Contains(
            "12345 bytes", lines);
        Assert.Contains("Created:", lines);
        Assert.Contains(
            "2026-08-03 14:22:01 UTC",
            lines);
        Assert.Contains("Summary:", lines);
        Assert.Contains(
            "Warnings: 1", lines);
        Assert.Contains(
            "Failures: 0", lines);
        Assert.Contains(
            "Preview Changes: True",
            lines);
        Assert.Contains(
            "Prefixes: 1946", lines);
        Assert.Contains(
            "DNS Domains: 8", lines);
    }

    [Fact]
    public void Render_HealthySummary_ShowsTrue()
    {
        SupportBundleExportResult result = new()
        {
            BundlePath = "C:\\bundle.zip",
            BytesWritten = 1,
            ExportedAt = DateTimeOffset.UtcNow,
            Snapshot = new SupportSnapshot
            {
                CapturedAt = DateTimeOffset.UtcNow,
                Summary = new SupportSnapshotSummary
                {
                    DiagnosticPassedCount = 5,
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

        List<string> lines = [
            ..PathVeer.Core.Cli.SupportBundleCliRenderer
                .Render(result)
        ];

        Assert.Contains("Healthy: True", lines);
    }

    [Fact]
    public void Render_NullResult_Throws()
    {
        IEnumerable<string> enumerable =
            PathVeer.Core.Cli.SupportBundleCliRenderer
                .Render(null!);

        Assert.Throws<ArgumentNullException>(
            () => enumerable.GetEnumerator().MoveNext());
    }
}
