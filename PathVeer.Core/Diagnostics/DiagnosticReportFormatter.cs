using System.Linq;
using System.Text;

namespace PathVeer.Core.Diagnostics;

public static class DiagnosticReportFormatter
{
    public static string Format(
        DiagnosticReport report,
        DiagnosticFormat format)
    {
        return format switch
        {
            DiagnosticFormat.Summary =>
                FormatSummary(report),
            DiagnosticFormat.Detailed =>
                FormatDetailed(report),
            DiagnosticFormat.Compact =>
                FormatCompact(report),
            _ => throw new ArgumentOutOfRangeException(
                nameof(format))
        };
    }

    private static string FormatSummary(
        DiagnosticReport report)
    {
        var sb = new StringBuilder();

        sb.Append("Health: ");
        sb.AppendLine(report.Healthy ? "OK" : "UNHEALTHY");

        sb.Append("Checks: ");
        sb.AppendLine(report.Results.Count.ToString());

        sb.Append("Passed: ");
        sb.AppendLine(report.PassedCount.ToString());

        sb.Append("Warnings: ");
        sb.AppendLine(report.WarningCount.ToString());

        sb.Append("Failed: ");
        sb.AppendLine(report.FailedCount.ToString());

        sb.Append("Highest severity: ");
        sb.AppendLine(
            report.HighestSeverity.ToString());

        return sb.ToString();
    }

    private static string FormatDetailed(
        DiagnosticReport report)
    {
        // Capacity hint: fixed header (~140 chars) plus an estimate per
        // result. Conservative over-allocation is harmless and only sizes
        // the initial buffer; output text is unchanged.
        var sb = new StringBuilder(
            140 + report.Results.Count * 96);

        sb.AppendLine("=== Diagnostic Report ===");
        sb.Append("Captured: ");
        sb.AppendLine(
            report.CapturedAt.ToString("o"));
        sb.Append("Health: ");
        sb.AppendLine(report.Healthy ? "OK" : "UNHEALTHY");
        sb.Append("Summary: ");
        sb.Append(report.PassedCount);
        sb.Append(" passed, ");
        sb.Append(report.WarningCount);
        sb.Append(" warnings, ");
        sb.Append(report.FailedCount);
        sb.AppendLine(" failed");
        sb.Append("Highest severity: ");
        sb.AppendLine(
            report.HighestSeverity.ToString());
        sb.AppendLine();

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

            sb.Append("[");
            sb.Append(category);
            sb.AppendLine("]");

            foreach (DiagnosticResult result in results)
            {
                string statusIcon =
                    result.Status switch
                    {
                        DiagnosticStatus.Passed => "+",
                        DiagnosticStatus.Warning => "!",
                        DiagnosticStatus.Failed => "X",
                        _ => "?"
                    };

                sb.Append("  [");
                sb.Append(statusIcon);
                sb.Append("] ");
                sb.Append(result.Id);
                sb.Append(": ");
                sb.AppendLine(result.Message);

                if (result.SuggestedAction is not null)
                {
                    sb.Append("       -> ");
                    sb.AppendLine(
                        result.SuggestedAction);
                }
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string FormatCompact(
        DiagnosticReport report)
    {
        var sb = new StringBuilder();

        sb.Append(report.Healthy ? "OK" : "FAIL");
        sb.Append(" | ");
        sb.Append(report.PassedCount);
        sb.Append("P ");
        sb.Append(report.WarningCount);
        sb.Append("W ");
        sb.Append(report.FailedCount);
        sb.Append("F");

        if (!report.Healthy)
        {
            IEnumerable<string> failedIds =
                report.Results
                    .Where(r =>
                        r.Status == DiagnosticStatus.Failed)
                    .Select(r => r.Id);

            sb.Append(" | ");
            sb.Append(string.Join(", ", failedIds));
        }

        return sb.ToString();
    }
}
