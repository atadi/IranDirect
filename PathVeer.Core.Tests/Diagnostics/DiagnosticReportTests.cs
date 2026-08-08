using PathVeer.Core.Diagnostics;

namespace PathVeer.Core.Tests.Diagnostics;

public sealed class DiagnosticReportTests
{
    private static DiagnosticResult MakeResult(
        string id,
        DiagnosticStatus status,
        DiagnosticSeverity severity,
        string message = "test")
    {
        return new DiagnosticResult(
            Id: id,
            Title: id,
            Status: status,
            Severity: severity,
            Message: message,
            SuggestedAction: null);
    }

    [Fact]
    public void EmptyResults_ReturnsPassSeverity()
    {
        var report = new DiagnosticReport(
            CapturedAt: DateTimeOffset.UtcNow,
            Results: []);

        Assert.Equal(
            DiagnosticSeverity.Pass,
            report.HighestSeverity);
    }

    [Fact]
    public void AllPassed_ReturnsInfoSeverity()
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
                    DiagnosticStatus.Passed,
                    DiagnosticSeverity.Pass)
            ]);

        Assert.Equal(
            DiagnosticSeverity.Info,
            report.HighestSeverity);
    }

    [Fact]
    public void WarningPresent_ReturnsWarningSeverity()
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

        Assert.Equal(
            DiagnosticSeverity.Warning,
            report.HighestSeverity);
    }

    [Fact]
    public void ErrorPresent_ReturnsErrorSeverity()
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
                    DiagnosticStatus.Failed,
                    DiagnosticSeverity.Error)
            ]);

        Assert.Equal(
            DiagnosticSeverity.Error,
            report.HighestSeverity);
    }

    [Fact]
    public void HighestSeverity_IsMaxOfAllSeverities()
    {
        var report = new DiagnosticReport(
            CapturedAt: DateTimeOffset.UtcNow,
            Results:
            [
                MakeResult(
                    "a",
                    DiagnosticStatus.Failed,
                    DiagnosticSeverity.Warning),
                MakeResult(
                    "b",
                    DiagnosticStatus.Failed,
                    DiagnosticSeverity.Error),
                MakeResult(
                    "c",
                    DiagnosticStatus.Passed,
                    DiagnosticSeverity.Pass)
            ]);

        Assert.Equal(
            DiagnosticSeverity.Error,
            report.HighestSeverity);
    }

    [Fact]
    public void Categories_GroupsResultsCorrectly()
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
                    DiagnosticStatus.Warning,
                    DiagnosticSeverity.Warning)
            ]);

        var categories = report.Categories;

        Assert.Single(
            categories[DiagnosticCategory.Configuration]);
        Assert.Single(
            categories[DiagnosticCategory.Runtime]);
        Assert.Single(
            categories[DiagnosticCategory.Routing]);
        Assert.Empty(
            categories[DiagnosticCategory.Vpn]);
    }

    [Fact]
    public void Summary_MatchesReportCounts()
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

        DiagnosticSummary summary = report.Summary;

        Assert.Equal(3, summary.TotalChecks);
        Assert.Equal(1, summary.PassedCount);
        Assert.Equal(1, summary.WarningCount);
        Assert.Equal(1, summary.FailedCount);
        Assert.False(summary.Healthy);
        Assert.Equal(
            DiagnosticSeverity.Error,
            summary.HighestSeverity);
    }

    [Fact]
    public void Summary_HealthyTrue_WhenNoWarningsOrFailures()
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

        DiagnosticSummary summary = report.Summary;

        Assert.True(summary.Healthy);
        Assert.Equal(1, summary.PassedCount);
        Assert.Equal(0, summary.WarningCount);
        Assert.Equal(0, summary.FailedCount);
    }

    [Fact]
    public void Categories_UnknownId_FallsBackToConfiguration()
    {
        var report = new DiagnosticReport(
            CapturedAt: DateTimeOffset.UtcNow,
            Results:
            [
                MakeResult(
                    "unknown-check-id",
                    DiagnosticStatus.Passed,
                    DiagnosticSeverity.Info)
            ]);

        var categories = report.Categories;

        Assert.Single(
            categories[DiagnosticCategory.Configuration]);
    }

    [Fact]
    public void Categories_EmptyResults_AllCategoriesEmpty()
    {
        var report = new DiagnosticReport(
            CapturedAt: DateTimeOffset.UtcNow,
            Results: []);

        var categories = report.Categories;

        foreach (DiagnosticCategory category in
            Enum.GetValues<DiagnosticCategory>())
        {
            Assert.Empty(categories[category]);
        }
    }

    [Fact]
    public void Summary_EmptyResults_AllCountsZero()
    {
        var report = new DiagnosticReport(
            CapturedAt: DateTimeOffset.UtcNow,
            Results: []);

        DiagnosticSummary summary = report.Summary;

        Assert.Equal(0, summary.TotalChecks);
        Assert.Equal(0, summary.PassedCount);
        Assert.Equal(0, summary.WarningCount);
        Assert.Equal(0, summary.FailedCount);
        Assert.True(summary.Healthy);
        Assert.Equal(
            DiagnosticSeverity.Pass,
            summary.HighestSeverity);
    }
}
