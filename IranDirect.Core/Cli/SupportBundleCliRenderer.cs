using IranDirect.Core.Support;

namespace IranDirect.Core.Cli;

public static class SupportBundleCliRenderer
{
    public static IEnumerable<string> Render(
        SupportBundleExportResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        SupportSnapshotSummary summary =
            result.Snapshot?.Summary
                ?? new SupportSnapshotSummary
                {
                    DiagnosticPassedCount = 0,
                    DiagnosticWarningCount = 0,
                    DiagnosticFailedCount = 0,
                    PrefixCount = 0,
                    DnsDomainCount = 0,
                    ExecutionPreviewHasChanges = false,
                    RuntimeAvailable = false,
                    PerformanceAvailable = false
                };

        yield return "=== Support Bundle ===";
        yield return string.Empty;
        yield return "Bundle:";
        yield return result.BundlePath;
        yield return string.Empty;
        yield return "Size:";
        yield return $"{result.BytesWritten} bytes";
        yield return string.Empty;
        yield return "Created:";
        yield return FormatTimestamp(result.ExportedAt);
        yield return string.Empty;

        yield return "Summary:";
        yield return string.Empty;
        yield return $"Healthy: {YesNo(summary.Healthy)}";
        yield return $"Warnings: {summary.DiagnosticWarningCount}";
        yield return $"Failures: {summary.DiagnosticFailedCount}";
        yield return
            $"Preview Changes: " +
            $"{YesNo(summary.ExecutionPreviewHasChanges)}";
        yield return $"Prefixes: {summary.PrefixCount}";
        yield return $"DNS Domains: {summary.DnsDomainCount}";
    }

    private static string YesNo(bool value) =>
        value ? "True" : "False";

    private static string FormatTimestamp(
        DateTimeOffset timestamp) =>
        timestamp.ToUniversalTime()
            .ToString("yyyy-MM-dd HH:mm:ss") +
        " UTC";
}
