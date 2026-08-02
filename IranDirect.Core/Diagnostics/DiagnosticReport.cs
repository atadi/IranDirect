using System.Collections.ObjectModel;

namespace IranDirect.Core.Diagnostics;

public sealed record DiagnosticReport(
    DateTimeOffset CapturedAt,
    IReadOnlyList<DiagnosticResult> Results)
{
    public int PassedCount =>
        Results.Count(r => r.Status == DiagnosticStatus.Passed);

    public int WarningCount =>
        Results.Count(r => r.Status == DiagnosticStatus.Warning);

    public int FailedCount =>
        Results.Count(r => r.Status == DiagnosticStatus.Failed);

    public bool Healthy =>
        FailedCount == 0 && WarningCount == 0;

    public DiagnosticSeverity HighestSeverity
    {
        get
        {
            if (Results.Count == 0)
            {
                return DiagnosticSeverity.Pass;
            }

            DiagnosticSeverity highest =
                DiagnosticSeverity.Pass;

            foreach (DiagnosticResult result in Results)
            {
                if (result.Severity > highest)
                {
                    highest = result.Severity;
                }
            }

            return highest;
        }
    }

    public IReadOnlyDictionary<DiagnosticCategory,
        IReadOnlyList<DiagnosticResult>> Categories =>
        DiagnosticCategoryMap.Default.GroupResults(Results);

    public DiagnosticSummary Summary => new(
        TotalChecks: Results.Count,
        PassedCount: PassedCount,
        WarningCount: WarningCount,
        FailedCount: FailedCount,
        Healthy: Healthy,
        HighestSeverity: HighestSeverity);
}
