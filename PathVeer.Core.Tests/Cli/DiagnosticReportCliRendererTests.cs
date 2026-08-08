using PathVeer.Core.Cli;
using PathVeer.Core.Diagnostics;

namespace PathVeer.Core.Tests.Cli;

public sealed class DiagnosticReportCliRendererTests
{
    private static DiagnosticResult MakeResult(
        string id,
        DiagnosticStatus status,
        DiagnosticSeverity severity,
        string? title = null,
        string? message = null,
        string? suggestedAction = null)
    {
        return new DiagnosticResult(
            Id: id,
            Title: title ?? id,
            Status: status,
            Severity: severity,
            Message: message ?? $"check {id}",
            SuggestedAction: suggestedAction);
    }

    [Fact]
    public void Summary_HealthyShowsYes()
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

        string[] lines = DiagnosticReportCliRenderer
            .Render(report, DiagnosticFormat.Summary)
            .ToArray();

        Assert.Contains("IranDirect Diagnostics", lines);
        Assert.Contains("Healthy: Yes", lines);
    }

    [Fact]
    public void Summary_UnhealthyShowsNo()
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

        string[] lines = DiagnosticReportCliRenderer
            .Render(report, DiagnosticFormat.Summary)
            .ToArray();

        Assert.Contains("Healthy: No", lines);
    }

    [Fact]
    public void Summary_ShowsCounts()
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

        string[] lines = DiagnosticReportCliRenderer
            .Render(report, DiagnosticFormat.Summary)
            .ToArray();

        Assert.Contains("Passed: 1", lines);
        Assert.Contains("Warnings: 1", lines);
        Assert.Contains("Failed: 1", lines);
    }

    [Fact]
    public void Summary_EmptyReport()
    {
        var report = new DiagnosticReport(
            CapturedAt: DateTimeOffset.UtcNow,
            Results: []);

        string[] lines = DiagnosticReportCliRenderer
            .Render(report, DiagnosticFormat.Summary)
            .ToArray();

        Assert.Contains("Healthy: Yes", lines);
        Assert.Contains("Passed: 0", lines);
        Assert.Contains("Warnings: 0", lines);
        Assert.Contains("Failed: 0", lines);
    }

    [Fact]
    public void Detailed_ShowsHeader()
    {
        var report = new DiagnosticReport(
            CapturedAt: DateTimeOffset.UtcNow,
            Results: []);

        string[] lines = DiagnosticReportCliRenderer
            .Render(report, DiagnosticFormat.Detailed)
            .ToArray();

        Assert.Contains("IranDirect Diagnostics", lines);
    }

    [Fact]
    public void Detailed_EmptyReport_ShowsNoChecks()
    {
        var report = new DiagnosticReport(
            CapturedAt: DateTimeOffset.UtcNow,
            Results: []);

        string[] lines = DiagnosticReportCliRenderer
            .Render(report, DiagnosticFormat.Detailed)
            .ToArray();

        Assert.Contains("No checks found.", lines);
    }

    [Fact]
    public void Detailed_GroupsByCategory()
    {
        var report = new DiagnosticReport(
            CapturedAt: DateTimeOffset.UtcNow,
            Results:
            [
                MakeResult(
                    "desired-configuration",
                    DiagnosticStatus.Passed,
                    DiagnosticSeverity.Info,
                    title: "Configuration valid"),
                MakeResult(
                    "runtime-state",
                    DiagnosticStatus.Passed,
                    DiagnosticSeverity.Info,
                    title: "Runtime state valid"),
                MakeResult(
                    "windows-route-table",
                    DiagnosticStatus.Warning,
                    DiagnosticSeverity.Warning,
                    title: "2 managed routes missing")
            ]);

        string[] lines = DiagnosticReportCliRenderer
            .Render(report, DiagnosticFormat.Detailed)
            .ToArray();

        Assert.Contains("Configuration", lines);
        Assert.Contains("-------------", lines);
        Assert.Contains("  + Configuration valid", lines);
        Assert.Contains("Runtime", lines);
        Assert.Contains("-------", lines);
        Assert.Contains("  + Runtime state valid", lines);
        Assert.Contains("Routing", lines);
        Assert.Contains("-------", lines);
        Assert.Contains(
            "  ! 2 managed routes missing", lines);
    }

    [Fact]
    public void Detailed_ShowsPassedIcon()
    {
        var report = new DiagnosticReport(
            CapturedAt: DateTimeOffset.UtcNow,
            Results:
            [
                MakeResult(
                    "a",
                    DiagnosticStatus.Passed,
                    DiagnosticSeverity.Info,
                    title: "Check A")
            ]);

        string[] lines = DiagnosticReportCliRenderer
            .Render(report, DiagnosticFormat.Detailed)
            .ToArray();

        Assert.Contains("  + Check A", lines);
    }

    [Fact]
    public void Detailed_ShowsWarningIcon()
    {
        var report = new DiagnosticReport(
            CapturedAt: DateTimeOffset.UtcNow,
            Results:
            [
                MakeResult(
                    "a",
                    DiagnosticStatus.Warning,
                    DiagnosticSeverity.Warning,
                    title: "Warning check")
            ]);

        string[] lines = DiagnosticReportCliRenderer
            .Render(report, DiagnosticFormat.Detailed)
            .ToArray();

        Assert.Contains("  ! Warning check", lines);
    }

    [Fact]
    public void Detailed_ShowsFailedIcon()
    {
        var report = new DiagnosticReport(
            CapturedAt: DateTimeOffset.UtcNow,
            Results:
            [
                MakeResult(
                    "a",
                    DiagnosticStatus.Failed,
                    DiagnosticSeverity.Error,
                    title: "Failed check")
            ]);

        string[] lines = DiagnosticReportCliRenderer
            .Render(report, DiagnosticFormat.Detailed)
            .ToArray();

        Assert.Contains("  X Failed check", lines);
    }

    [Fact]
    public void Detailed_ShowsSummaryAtEnd()
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

        string[] lines = DiagnosticReportCliRenderer
            .Render(report, DiagnosticFormat.Detailed)
            .ToArray();

        Assert.Contains("Summary", lines);
        Assert.Contains("Warnings: 1", lines);
        Assert.Contains("Failed: 0", lines);
    }

    [Fact]
    public void Detailed_OnlyNonEmptyCategories()
    {
        var report = new DiagnosticReport(
            CapturedAt: DateTimeOffset.UtcNow,
            Results:
            [
                MakeResult(
                    "desired-configuration",
                    DiagnosticStatus.Passed,
                    DiagnosticSeverity.Info,
                    title: "Config OK")
            ]);

        string[] lines = DiagnosticReportCliRenderer
            .Render(report, DiagnosticFormat.Detailed)
            .ToArray();

        Assert.Contains("Configuration", lines);
        Assert.DoesNotContain("Runtime", lines);
        Assert.DoesNotContain("Routing", lines);
        Assert.DoesNotContain("Vpn", lines);
        Assert.DoesNotContain("Prefixes", lines);
        Assert.DoesNotContain("Updates", lines);
    }

    [Fact]
    public void Detailed_EmptyCategories_NoSeparatorLines()
    {
        var report = new DiagnosticReport(
            CapturedAt: DateTimeOffset.UtcNow,
            Results: []);

        string[] lines = DiagnosticReportCliRenderer
            .Render(report, DiagnosticFormat.Detailed)
            .ToArray();

        int separatorCount = lines.Count(
            l => l.Length > 0 && l.All(c => c == '-'));

        Assert.Equal(0, separatorCount);
    }

    [Fact]
    public void Compact_HealthyShowsOK()
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

        string[] lines = DiagnosticReportCliRenderer
            .Render(report, DiagnosticFormat.Compact)
            .ToArray();

        Assert.StartsWith("OK", lines[0]);
    }

    [Fact]
    public void Compact_UnhealthyShowsFAIL()
    {
        var report = new DiagnosticReport(
            CapturedAt: DateTimeOffset.UtcNow,
            Results:
            [
                MakeResult(
                    "a",
                    DiagnosticStatus.Failed,
                    DiagnosticSeverity.Error,
                    title: "Broken check")
            ]);

        string[] lines = DiagnosticReportCliRenderer
            .Render(report, DiagnosticFormat.Compact)
            .ToArray();

        Assert.StartsWith("FAIL", lines[0]);
        Assert.Contains("Broken check", lines[1]);
    }

    [Fact]
    public void Compact_ShowsCounts()
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

        string[] lines = DiagnosticReportCliRenderer
            .Render(report, DiagnosticFormat.Compact)
            .ToArray();

        Assert.Contains("1P", lines[0]);
        Assert.Contains("1W", lines[0]);
        Assert.Contains("1F", lines[0]);
    }

    [Fact]
    public void Compact_Healthy_NoSecondLine()
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

        string[] lines = DiagnosticReportCliRenderer
            .Render(report, DiagnosticFormat.Compact)
            .ToArray();

        Assert.Single(lines);
    }

    [Fact]
    public void NullReport_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => DiagnosticReportCliRenderer
                .Render(null!)
                .ToArray());
    }

    [Fact]
    public void InvalidFormat_Throws()
    {
        var report = new DiagnosticReport(
            CapturedAt: DateTimeOffset.UtcNow,
            Results: []);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => DiagnosticReportCliRenderer
                .Render(
                    report,
                    (DiagnosticFormat)999)
                .ToArray());
    }

    [Fact]
    public void Detailed_CategoriesAreInEnumOrder()
    {
        var report = new DiagnosticReport(
            CapturedAt: DateTimeOffset.UtcNow,
            Results:
            [
                MakeResult(
                    "windows-route-table",
                    DiagnosticStatus.Passed,
                    DiagnosticSeverity.Info,
                    title: "Route OK"),
                MakeResult(
                    "desired-configuration",
                    DiagnosticStatus.Passed,
                    DiagnosticSeverity.Info,
                    title: "Config OK"),
                MakeResult(
                    "runtime-state",
                    DiagnosticStatus.Passed,
                    DiagnosticSeverity.Info,
                    title: "Runtime OK")
            ]);

        string[] lines = DiagnosticReportCliRenderer
            .Render(report, DiagnosticFormat.Detailed)
            .ToArray();

        int configIndex = Array.FindIndex(
            lines, l => l == "Configuration");
        int runtimeIndex = Array.FindIndex(
            lines, l => l == "Runtime");
        int routingIndex = Array.FindIndex(
            lines, l => l == "Routing");

        Assert.True(configIndex < runtimeIndex);
        Assert.True(runtimeIndex < routingIndex);
    }
}
