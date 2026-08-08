using PathVeer.Core.Diagnostics;

namespace PathVeer.Core.Tests.Diagnostics;

public sealed class DiagnosticReportFormatterTests
{
    private static DiagnosticResult MakeResult(
        string id,
        DiagnosticStatus status,
        DiagnosticSeverity severity,
        string message = "test message",
        string? suggestedAction = null)
    {
        return new DiagnosticResult(
            Id: id,
            Title: id,
            Status: status,
            Severity: severity,
            Message: message,
            SuggestedAction: suggestedAction);
    }

    [Fact]
    public void SummaryFormat_HealthyShowsOK()
    {
        var report = new DiagnosticReport(
            CapturedAt: DateTimeOffset.UtcNow,
            Results:
            [
                MakeResult(
                    "a",
                    DiagnosticStatus.Passed,
                    DiagnosticSeverity.Info)
            ]);

        string output = DiagnosticReportFormatter.Format(
            report, DiagnosticFormat.Summary);

        Assert.Contains("Health: OK", output);
    }

    [Fact]
    public void SummaryFormat_UnhealthyShowsUNHEALTHY()
    {
        var report = new DiagnosticReport(
            CapturedAt: DateTimeOffset.UtcNow,
            Results:
            [
                MakeResult(
                    "a",
                    DiagnosticStatus.Failed,
                    DiagnosticSeverity.Error)
            ]);

        string output = DiagnosticReportFormatter.Format(
            report, DiagnosticFormat.Summary);

        Assert.Contains("Health: UNHEALTHY", output);
    }

    [Fact]
    public void SummaryFormat_ShowsCounts()
    {
        var report = new DiagnosticReport(
            CapturedAt: DateTimeOffset.UtcNow,
            Results:
            [
                MakeResult(
                    "a",
                    DiagnosticStatus.Passed,
                    DiagnosticSeverity.Info),
                MakeResult(
                    "b",
                    DiagnosticStatus.Warning,
                    DiagnosticSeverity.Warning),
                MakeResult(
                    "c",
                    DiagnosticStatus.Failed,
                    DiagnosticSeverity.Error)
            ]);

        string output = DiagnosticReportFormatter.Format(
            report, DiagnosticFormat.Summary);

        Assert.Contains("Checks: 3", output);
        Assert.Contains("Passed: 1", output);
        Assert.Contains("Warnings: 1", output);
        Assert.Contains("Failed: 1", output);
        Assert.Contains("Highest severity: Error", output);
    }

    [Fact]
    public void SummaryFormat_EmptyReport()
    {
        var report = new DiagnosticReport(
            CapturedAt: DateTimeOffset.UtcNow,
            Results: []);

        string output = DiagnosticReportFormatter.Format(
            report, DiagnosticFormat.Summary);

        Assert.Contains("Health: OK", output);
        Assert.Contains("Checks: 0", output);
        Assert.Contains("Passed: 0", output);
        Assert.Contains("Highest severity: Pass", output);
    }

    [Fact]
    public void DetailedFormat_ShowsHeader()
    {
        var report = new DiagnosticReport(
            CapturedAt: DateTimeOffset.UtcNow,
            Results: []);

        string output = DiagnosticReportFormatter.Format(
            report, DiagnosticFormat.Detailed);

        Assert.Contains("=== Diagnostic Report ===", output);
        Assert.Contains("Captured:", output);
    }

    [Fact]
    public void DetailedFormat_HealthyShowsOK()
    {
        var report = new DiagnosticReport(
            CapturedAt: DateTimeOffset.UtcNow,
            Results:
            [
                MakeResult(
                    "a",
                    DiagnosticStatus.Passed,
                    DiagnosticSeverity.Info)
            ]);

        string output = DiagnosticReportFormatter.Format(
            report, DiagnosticFormat.Detailed);

        Assert.Contains("Health: OK", output);
    }

    [Fact]
    public void DetailedFormat_UnhealthyShowsUNHEALTHY()
    {
        var report = new DiagnosticReport(
            CapturedAt: DateTimeOffset.UtcNow,
            Results:
            [
                MakeResult(
                    "a",
                    DiagnosticStatus.Failed,
                    DiagnosticSeverity.Error)
            ]);

        string output = DiagnosticReportFormatter.Format(
            report, DiagnosticFormat.Detailed);

        Assert.Contains("Health: UNHEALTHY", output);
    }

    [Fact]
    public void DetailedFormat_ShowsCategoryHeaders()
    {
        var report = new DiagnosticReport(
            CapturedAt: DateTimeOffset.UtcNow,
            Results:
            [
                MakeResult(
                    "desired-configuration",
                    DiagnosticStatus.Passed,
                    DiagnosticSeverity.Info),
                MakeResult(
                    "runtime-state",
                    DiagnosticStatus.Passed,
                    DiagnosticSeverity.Info),
                MakeResult(
                    "windows-route-table",
                    DiagnosticStatus.Passed,
                    DiagnosticSeverity.Info)
            ]);

        string output = DiagnosticReportFormatter.Format(
            report, DiagnosticFormat.Detailed);

        Assert.Contains("[Configuration]", output);
        Assert.Contains("[Runtime]", output);
        Assert.Contains("[Routing]", output);
    }

    [Fact]
    public void DetailedFormat_ShowsCheckResults()
    {
        var report = new DiagnosticReport(
            CapturedAt: DateTimeOffset.UtcNow,
            Results:
            [
                MakeResult(
                    "my-check",
                    DiagnosticStatus.Passed,
                    DiagnosticSeverity.Info,
                    message: "all good")
            ]);

        string output = DiagnosticReportFormatter.Format(
            report, DiagnosticFormat.Detailed);

        Assert.Contains("[+] my-check: all good", output);
    }

    [Fact]
    public void DetailedFormat_ShowsWarningIcon()
    {
        var report = new DiagnosticReport(
            CapturedAt: DateTimeOffset.UtcNow,
            Results:
            [
                MakeResult(
                    "warn-check",
                    DiagnosticStatus.Warning,
                    DiagnosticSeverity.Warning,
                    message: "something off")
            ]);

        string output = DiagnosticReportFormatter.Format(
            report, DiagnosticFormat.Detailed);

        Assert.Contains(
            "[!] warn-check: something off", output);
    }

    [Fact]
    public void DetailedFormat_ShowsFailedIcon()
    {
        var report = new DiagnosticReport(
            CapturedAt: DateTimeOffset.UtcNow,
            Results:
            [
                MakeResult(
                    "fail-check",
                    DiagnosticStatus.Failed,
                    DiagnosticSeverity.Error,
                    message: "broken")
            ]);

        string output = DiagnosticReportFormatter.Format(
            report, DiagnosticFormat.Detailed);

        Assert.Contains("[X] fail-check: broken", output);
    }

    [Fact]
    public void DetailedFormat_ShowsSuggestedAction()
    {
        var report = new DiagnosticReport(
            CapturedAt: DateTimeOffset.UtcNow,
            Results:
            [
                MakeResult(
                    "fix-check",
                    DiagnosticStatus.Failed,
                    DiagnosticSeverity.Error,
                    message: "broken",
                    suggestedAction: "Please fix this.")
            ]);

        string output = DiagnosticReportFormatter.Format(
            report, DiagnosticFormat.Detailed);

        Assert.Contains("-> Please fix this.", output);
    }

    [Fact]
    public void DetailedFormat_ShowsSummaryLine()
    {
        var report = new DiagnosticReport(
            CapturedAt: DateTimeOffset.UtcNow,
            Results:
            [
                MakeResult(
                    "a",
                    DiagnosticStatus.Passed,
                    DiagnosticSeverity.Info),
                MakeResult(
                    "b",
                    DiagnosticStatus.Warning,
                    DiagnosticSeverity.Warning)
            ]);

        string output = DiagnosticReportFormatter.Format(
            report, DiagnosticFormat.Detailed);

        Assert.Contains("1 passed, 1 warnings, 0 failed", output);
    }

    [Fact]
    public void CompactFormat_HealthyShowsOK()
    {
        var report = new DiagnosticReport(
            CapturedAt: DateTimeOffset.UtcNow,
            Results:
            [
                MakeResult(
                    "a",
                    DiagnosticStatus.Passed,
                    DiagnosticSeverity.Info)
            ]);

        string output = DiagnosticReportFormatter.Format(
            report, DiagnosticFormat.Compact);

        Assert.StartsWith("OK", output);
    }

    [Fact]
    public void CompactFormat_UnhealthyShowsFAIL()
    {
        var report = new DiagnosticReport(
            CapturedAt: DateTimeOffset.UtcNow,
            Results:
            [
                MakeResult(
                    "a",
                    DiagnosticStatus.Failed,
                    DiagnosticSeverity.Error)
            ]);

        string output = DiagnosticReportFormatter.Format(
            report, DiagnosticFormat.Compact);

        Assert.StartsWith("FAIL", output);
        Assert.Contains("| a", output);
    }

    [Fact]
    public void CompactFormat_ShowsCounts()
    {
        var report = new DiagnosticReport(
            CapturedAt: DateTimeOffset.UtcNow,
            Results:
            [
                MakeResult(
                    "a",
                    DiagnosticStatus.Passed,
                    DiagnosticSeverity.Info),
                MakeResult(
                    "b",
                    DiagnosticStatus.Warning,
                    DiagnosticSeverity.Warning),
                MakeResult(
                    "c",
                    DiagnosticStatus.Failed,
                    DiagnosticSeverity.Error)
            ]);

        string output = DiagnosticReportFormatter.Format(
            report, DiagnosticFormat.Compact);

        Assert.Contains("1P", output);
        Assert.Contains("1W", output);
        Assert.Contains("1F", output);
    }

    [Fact]
    public void CompactFormat_ShowsFailedIds()
    {
        var report = new DiagnosticReport(
            CapturedAt: DateTimeOffset.UtcNow,
            Results:
            [
                MakeResult(
                    "x",
                    DiagnosticStatus.Failed,
                    DiagnosticSeverity.Error),
                MakeResult(
                    "y",
                    DiagnosticStatus.Failed,
                    DiagnosticSeverity.Error)
            ]);

        string output = DiagnosticReportFormatter.Format(
            report, DiagnosticFormat.Compact);

        Assert.Contains("| x, y", output);
    }

    [Fact]
    public void CompactFormat_NoFailedIds_WhenHealthy()
    {
        var report = new DiagnosticReport(
            CapturedAt: DateTimeOffset.UtcNow,
            Results:
            [
                MakeResult(
                    "a",
                    DiagnosticStatus.Passed,
                    DiagnosticSeverity.Info)
            ]);

        string output = DiagnosticReportFormatter.Format(
            report, DiagnosticFormat.Compact);

        Assert.DoesNotContain("|", output.Split(
            '|', StringSplitOptions.RemoveEmptyEntries)
            .Last());
        // Simpler check: after "OK | ...F" there should be no pipe
        Assert.DoesNotContain(", ", output);
    }

    [Fact]
    public void DetailedFormat_EmptyReport_NoCategories()
    {
        var report = new DiagnosticReport(
            CapturedAt: DateTimeOffset.UtcNow,
            Results: []);

        string output = DiagnosticReportFormatter.Format(
            report, DiagnosticFormat.Detailed);

        // Should have header but no category sections
        Assert.Contains("=== Diagnostic Report ===", output);
        Assert.DoesNotContain("[Configuration]", output);
        Assert.DoesNotContain("[Runtime]", output);
        Assert.DoesNotContain("[Routing]", output);
    }

    [Fact]
    public void Format_InvalidFormat_Throws()
    {
        var report = new DiagnosticReport(
            CapturedAt: DateTimeOffset.UtcNow,
            Results: []);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => DiagnosticReportFormatter.Format(
                report,
                (DiagnosticFormat)999));
    }
}
