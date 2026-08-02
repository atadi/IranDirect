using IranDirect.Core.Diagnostics;

namespace IranDirect.Tray;

public sealed record DiagnosticOverallStatus(
    string Text,
    bool IsHealthy);

public sealed record DiagnosticSummaryRow(
    string Label,
    string Value);

public sealed record DiagnosticCheckRow(
    string StatusIcon,
    string Title,
    string Message,
    string? SuggestedAction);

public sealed record DiagnosticCategorySection(
    string CategoryName,
    IReadOnlyList<DiagnosticCheckRow> Checks);

public sealed record DiagnosticReportDisplay(
    DiagnosticOverallStatus OverallStatus,
    IReadOnlyList<DiagnosticSummaryRow> Summary,
    IReadOnlyList<DiagnosticCategorySection> Categories);

public static class DiagnosticReportDialogModel
{
    public static DiagnosticReportDisplay Map(
        DiagnosticReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        DiagnosticOverallStatus overall = MapOverallStatus(report);
        IReadOnlyList<DiagnosticSummaryRow> summary =
            MapSummary(report);
        IReadOnlyList<DiagnosticCategorySection> categories =
            MapCategories(report);

        return new DiagnosticReportDisplay(
            overall, summary, categories);
    }

    public static string GetCopyText(DiagnosticReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        return DiagnosticReportFormatter.Format(
            report,
            DiagnosticFormat.Detailed);
    }

    private static DiagnosticOverallStatus MapOverallStatus(
        DiagnosticReport report)
    {
        if (report.FailedCount > 0)
        {
            return new DiagnosticOverallStatus(
                "Failed", IsHealthy: false);
        }

        if (report.WarningCount > 0)
        {
            return new DiagnosticOverallStatus(
                "Warnings", IsHealthy: false);
        }

        return new DiagnosticOverallStatus(
            "Healthy", IsHealthy: true);
    }

    private static IReadOnlyList<DiagnosticSummaryRow> MapSummary(
        DiagnosticReport report)
    {
        return
        [
            new DiagnosticSummaryRow(
                "Passed",
                report.PassedCount.ToString()),
            new DiagnosticSummaryRow(
                "Warnings",
                report.WarningCount.ToString()),
            new DiagnosticSummaryRow(
                "Failed",
                report.FailedCount.ToString()),
            new DiagnosticSummaryRow(
                "Captured at",
                report.CapturedAt.ToLocalTime()
                    .ToString("yyyy-MM-dd HH:mm:ss"))
        ];
    }

    private static IReadOnlyList<DiagnosticCategorySection>
        MapCategories(DiagnosticReport report)
    {
        var sections =
            new List<DiagnosticCategorySection>();

        IReadOnlyDictionary<DiagnosticCategory,
            IReadOnlyList<DiagnosticResult>> categories =
            report.Categories;

        foreach (DiagnosticCategory category in
            Enum.GetValues<DiagnosticCategory>())
        {
            if (!categories.TryGetValue(
                    category,
                    out IReadOnlyList<DiagnosticResult>?
                        results) ||
                results.Count == 0)
            {
                continue;
            }

            List<DiagnosticCheckRow> checks = [];

            foreach (DiagnosticResult result in results)
            {
                checks.Add(new DiagnosticCheckRow(
                    StatusIcon(result.Status),
                    result.Title,
                    result.Message,
                    result.SuggestedAction));
            }

            sections.Add(
                new DiagnosticCategorySection(
                    category.ToString(), checks));
        }

        return sections;
    }

    private static string StatusIcon(
        DiagnosticStatus status) =>
        status switch
        {
            DiagnosticStatus.Passed => "+",
            DiagnosticStatus.Warning => "!",
            DiagnosticStatus.Failed => "X",
            _ => "?"
        };
}
