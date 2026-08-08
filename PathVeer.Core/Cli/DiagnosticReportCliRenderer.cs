using PathVeer.Core.Diagnostics;

namespace PathVeer.Core.Cli;

public static class DiagnosticReportCliRenderer
{
    public static IEnumerable<string> Render(
        DiagnosticReport report,
        DiagnosticFormat format = DiagnosticFormat.Detailed)
    {
        ArgumentNullException.ThrowIfNull(report);

        return format switch
        {
            DiagnosticFormat.Summary =>
                RenderSummary(report),
            DiagnosticFormat.Detailed =>
                RenderDetailed(report),
            DiagnosticFormat.Compact =>
                RenderCompact(report),
            _ => throw new ArgumentOutOfRangeException(
                nameof(format))
        };
    }

    private static IEnumerable<string> RenderSummary(
        DiagnosticReport report)
    {
        yield return "PathVeer Diagnostics";
        yield return string.Empty;
        yield return
            $"Healthy: {(report.Healthy ? "Yes" : "No")}";
        yield return string.Empty;
        yield return $"Passed: {report.PassedCount}";
        yield return $"Warnings: {report.WarningCount}";
        yield return $"Failed: {report.FailedCount}";
    }

    private static IEnumerable<string> RenderDetailed(
        DiagnosticReport report)
    {
        yield return "PathVeer Diagnostics";
        yield return string.Empty;

        IReadOnlyDictionary<DiagnosticCategory,
            IReadOnlyList<DiagnosticResult>> categories =
            report.Categories;

        bool hasAnyResults = false;

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

            hasAnyResults = true;

            yield return category.ToString();
            yield return new string(
                '-', category.ToString().Length);

            foreach (DiagnosticResult result in results)
            {
                string icon = result.Status switch
                {
                    DiagnosticStatus.Passed => "+",
                    DiagnosticStatus.Warning => "!",
                    DiagnosticStatus.Failed => "X",
                    _ => "?"
                };

                yield return
                    $"  {icon} {result.Title}";
            }

            yield return string.Empty;
        }

        if (!hasAnyResults)
        {
            yield return "No checks found.";
            yield return string.Empty;
        }

        yield return "Summary";
        yield return string.Empty;
        yield return $"Warnings: {report.WarningCount}";
        yield return $"Failed: {report.FailedCount}";
    }

    private static IEnumerable<string> RenderCompact(
        DiagnosticReport report)
    {
        string status = report.Healthy ? "OK" : "FAIL";

        yield return
            $"{status} | " +
            $"{report.PassedCount}P " +
            $"{report.WarningCount}W " +
            $"{report.FailedCount}F";

        if (!report.Healthy)
        {
            IEnumerable<string> failedIds =
                report.Results
                    .Where(r =>
                        r.Status == DiagnosticStatus.Failed)
                    .Select(r => r.Title);

            yield return
                $"Failed: {string.Join(", ", failedIds)}";
        }
    }
}
