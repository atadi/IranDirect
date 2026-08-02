using IranDirect.Core.Diagnostics;
using IranDirect.Tray;

namespace IranDirect.Core.Tests.Tray;

public sealed class DiagnosticReportDialogModelTests
{
    private static DiagnosticResult MakeResult(
        string id,
        DiagnosticStatus status,
        string title = "test",
        string message = "test message",
        string? suggestedAction = null) =>
        new(
            id,
            title,
            status,
            status switch
            {
                DiagnosticStatus.Passed =>
                    DiagnosticSeverity.Pass,
                DiagnosticStatus.Warning =>
                    DiagnosticSeverity.Warning,
                DiagnosticStatus.Failed =>
                    DiagnosticSeverity.Error,
                _ => DiagnosticSeverity.Info
            },
            message,
            suggestedAction);

    [Fact]
    public void Map_HealthyReport_ShowsHealthyStatus()
    {
        DiagnosticReport report = new(
            DateTimeOffset.UtcNow,
            [
                MakeResult(
                    "a",
                    DiagnosticStatus.Passed,
                    title: "Check A")
            ]);

        DiagnosticReportDisplay display =
            DiagnosticReportDialogModel.Map(report);

        Assert.Equal("Healthy", display.OverallStatus.Text);
        Assert.True(display.OverallStatus.IsHealthy);
    }

    [Fact]
    public void Map_WarningsReport_ShowsWarningsStatus()
    {
        DiagnosticReport report = new(
            DateTimeOffset.UtcNow,
            [
                MakeResult(
                    "a",
                    DiagnosticStatus.Passed),
                MakeResult(
                    "b",
                    DiagnosticStatus.Warning,
                    title: "Warning check")
            ]);

        DiagnosticReportDisplay display =
            DiagnosticReportDialogModel.Map(report);

        Assert.Equal("Warnings", display.OverallStatus.Text);
        Assert.False(display.OverallStatus.IsHealthy);
    }

    [Fact]
    public void Map_FailedReport_ShowsFailedStatus()
    {
        DiagnosticReport report = new(
            DateTimeOffset.UtcNow,
            [
                MakeResult(
                    "a",
                    DiagnosticStatus.Failed,
                    title: "Failed check")
            ]);

        DiagnosticReportDisplay display =
            DiagnosticReportDialogModel.Map(report);

        Assert.Equal("Failed", display.OverallStatus.Text);
        Assert.False(display.OverallStatus.IsHealthy);
    }

    [Fact]
    public void Map_EmptyReport_ShowsHealthy()
    {
        DiagnosticReport report = new(
            DateTimeOffset.UtcNow, []);

        DiagnosticReportDisplay display =
            DiagnosticReportDialogModel.Map(report);

        Assert.Equal("Healthy", display.OverallStatus.Text);
        Assert.True(display.OverallStatus.IsHealthy);
    }

    [Fact]
    public void Map_ShowsSummaryCounts()
    {
        DiagnosticReport report = new(
            DateTimeOffset.UtcNow,
            [
                MakeResult("a", DiagnosticStatus.Passed),
                MakeResult("b", DiagnosticStatus.Passed),
                MakeResult("c", DiagnosticStatus.Warning),
                MakeResult("d", DiagnosticStatus.Failed)
            ]);

        DiagnosticReportDisplay display =
            DiagnosticReportDialogModel.Map(report);

        Assert.Equal(
            "2",
            display.Summary
                .First(s => s.Label == "Passed").Value);
        Assert.Equal(
            "1",
            display.Summary
                .First(s => s.Label == "Warnings").Value);
        Assert.Equal(
            "1",
            display.Summary
                .First(s => s.Label == "Failed").Value);
    }

    [Fact]
    public void Map_ShowsCapturedAtTimestamp()
    {
        DateTimeOffset capturedAt = new(
            2026, 8, 5, 14, 30, 0, TimeSpan.Zero);
        DiagnosticReport report = new(capturedAt, []);

        DiagnosticReportDisplay display =
            DiagnosticReportDialogModel.Map(report);

        string capturedValue = display.Summary
            .First(s => s.Label == "Captured at").Value;

        string expected =
            capturedAt.ToLocalTime()
                .ToString("yyyy-MM-dd HH:mm:ss");

        Assert.Equal(expected, capturedValue);
    }

    [Fact]
    public void Map_GroupsResultsByCategory()
    {
        DiagnosticReport report = new(
            DateTimeOffset.UtcNow,
            [
                MakeResult(
                    "desired-configuration",
                    DiagnosticStatus.Passed,
                    title: "Config OK"),
                MakeResult(
                    "runtime-state",
                    DiagnosticStatus.Passed,
                    title: "Runtime OK"),
                MakeResult(
                    "windows-route-table",
                    DiagnosticStatus.Warning,
                    title: "Route warning"),
                MakeResult(
                    "desired-configuration",
                    DiagnosticStatus.Passed,
                    title: "Config OK 2")
            ]);

        DiagnosticReportDisplay display =
            DiagnosticReportDialogModel.Map(report);

        Assert.Equal(3, display.Categories.Count);

        DiagnosticCategorySection config =
            display.Categories
                .First(c => c.CategoryName == "Configuration");
        Assert.Equal(2, config.Checks.Count);

        DiagnosticCategorySection runtime =
            display.Categories
                .First(c => c.CategoryName == "Runtime");
        Assert.Single(runtime.Checks);

        DiagnosticCategorySection routing =
            display.Categories
                .First(c => c.CategoryName == "Routing");
        Assert.Single(routing.Checks);
    }

    [Fact]
    public void Map_PreservesRegistrationOrder()
    {
        DiagnosticReport report = new(
            DateTimeOffset.UtcNow,
            [
                MakeResult(
                    "windows-route-table",
                    DiagnosticStatus.Passed,
                    title: "Route OK"),
                MakeResult(
                    "desired-configuration",
                    DiagnosticStatus.Passed,
                    title: "Config OK"),
                MakeResult(
                    "runtime-state",
                    DiagnosticStatus.Passed,
                    title: "Runtime OK")
            ]);

        DiagnosticReportDisplay display =
            DiagnosticReportDialogModel.Map(report);

        Assert.Equal(
            "Configuration",
            display.Categories[0].CategoryName);
        Assert.Equal(
            "Runtime",
            display.Categories[1].CategoryName);
        Assert.Equal(
            "Routing",
            display.Categories[2].CategoryName);
    }

    [Fact]
    public void Map_ShowsSuggestedAction()
    {
        DiagnosticReport report = new(
            DateTimeOffset.UtcNow,
            [
                MakeResult(
                    "a",
                    DiagnosticStatus.Warning,
                    title: "Warning",
                    message: "Something is off",
                    suggestedAction: "Fix it now")
            ]);

        DiagnosticReportDisplay display =
            DiagnosticReportDialogModel.Map(report);

        DiagnosticCheckRow check =
            display.Categories[0].Checks[0];

        Assert.Equal("Fix it now", check.SuggestedAction);
    }

    [Fact]
    public void Map_NullSuggestedAction_IsNull()
    {
        DiagnosticReport report = new(
            DateTimeOffset.UtcNow,
            [
                MakeResult(
                    "a",
                    DiagnosticStatus.Passed,
                    title: "Check",
                    suggestedAction: null)
            ]);

        DiagnosticReportDisplay display =
            DiagnosticReportDialogModel.Map(report);

        DiagnosticCheckRow check =
            display.Categories[0].Checks[0];

        Assert.Null(check.SuggestedAction);
    }

    [Fact]
    public void Map_EmptyReport_HasNoCategories()
    {
        DiagnosticReport report = new(
            DateTimeOffset.UtcNow, []);

        DiagnosticReportDisplay display =
            DiagnosticReportDialogModel.Map(report);

        Assert.Empty(display.Categories);
    }

    [Fact]
    public void Map_ShowsStatusIcons()
    {
        DiagnosticReport report = new(
            DateTimeOffset.UtcNow,
            [
                MakeResult("a", DiagnosticStatus.Passed),
                MakeResult("b", DiagnosticStatus.Warning),
                MakeResult("c", DiagnosticStatus.Failed)
            ]);

        DiagnosticReportDisplay display =
            DiagnosticReportDialogModel.Map(report);

        List<DiagnosticCheckRow> allChecks =
            display.Categories
                .SelectMany(c => c.Checks)
                .ToList();

        Assert.Contains(
            allChecks,
            c => c.StatusIcon == "+");
        Assert.Contains(
            allChecks,
            c => c.StatusIcon == "!");
        Assert.Contains(
            allChecks,
            c => c.StatusIcon == "X");
    }

    [Fact]
    public void Map_NullReport_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => DiagnosticReportDialogModel.Map(null!));
    }

    [Fact]
    public void GetCopyText_UsesDetailedFormat()
    {
        DiagnosticReport report = new(
            DateTimeOffset.UtcNow,
            [
                MakeResult(
                    "a",
                    DiagnosticStatus.Passed,
                    title: "Check A",
                    message: "all good")
            ]);

        string text =
            DiagnosticReportDialogModel.GetCopyText(report);

        Assert.Contains("=== Diagnostic Report ===", text);
        Assert.Contains("[Configuration]", text);
        Assert.Contains("[+] a: all good", text);
    }

    [Fact]
    public void GetCopyText_NullReport_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => DiagnosticReportDialogModel.GetCopyText(
                null!));
    }

    [Fact]
    public void Map_BothWarningsAndFailures_ShowsFailed()
    {
        DiagnosticReport report = new(
            DateTimeOffset.UtcNow,
            [
                MakeResult(
                    "a",
                    DiagnosticStatus.Warning),
                MakeResult(
                    "b",
                    DiagnosticStatus.Failed)
            ]);

        DiagnosticReportDisplay display =
            DiagnosticReportDialogModel.Map(report);

        Assert.Equal("Failed", display.OverallStatus.Text);
        Assert.False(display.OverallStatus.IsHealthy);
    }

    [Fact]
    public void Map_SkipsEmptyCategories()
    {
        DiagnosticReport report = new(
            DateTimeOffset.UtcNow,
            [
                MakeResult(
                    "desired-configuration",
                    DiagnosticStatus.Passed)
            ]);

        DiagnosticReportDisplay display =
            DiagnosticReportDialogModel.Map(report);

        Assert.Single(display.Categories);
        Assert.Equal(
            "Configuration",
            display.Categories[0].CategoryName);
    }
}
