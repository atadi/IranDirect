using System.Collections.ObjectModel;

namespace PathVeer.Core.Diagnostics;

/// <summary>
/// Immutable diagnostic report. The result collection is defensively copied
/// once at construction and the aggregate <see cref="DiagnosticSummary"/> is
/// computed exactly once, so every summary/counter/severity/health read is
/// O(1) and allocation-free thereafter, and the values cannot be invalidated
/// by mutations to a collection a caller happened to retain.
/// </summary>
public sealed record DiagnosticReport
{
    private readonly DiagnosticResult[] _results;
    private readonly DiagnosticSummary _summary;
    private readonly IReadOnlyDictionary<DiagnosticCategory,
        IReadOnlyList<DiagnosticResult>> _categories;

    public DiagnosticReport(
        DateTimeOffset CapturedAt,
        IReadOnlyList<DiagnosticResult> Results)
    {
        ArgumentNullException.ThrowIfNull(Results);

        _results = new DiagnosticResult[Results.Count];
        for (int i = 0; i < Results.Count; i++)
        {
            _results[i] = Results[i];
        }

        this.CapturedAt = CapturedAt;
        this.Results = _results;
        _summary = ComputeSummary(_results);
        _categories =
            DiagnosticCategoryMap.Default.GroupResults(_results);
    }

    public DateTimeOffset CapturedAt { get; }

    public IReadOnlyList<DiagnosticResult> Results { get; }

    public int PassedCount => _summary.PassedCount;

    public int WarningCount => _summary.WarningCount;

    public int FailedCount => _summary.FailedCount;

    public bool Healthy => _summary.Healthy;

    public DiagnosticSeverity HighestSeverity => _summary.HighestSeverity;

    public IReadOnlyDictionary<DiagnosticCategory,
        IReadOnlyList<DiagnosticResult>> Categories =>
        _categories;

    public DiagnosticSummary Summary => _summary;

    private static DiagnosticSummary ComputeSummary(
        DiagnosticResult[] results)
    {
        int passed = 0;
        int warning = 0;
        int failed = 0;
        DiagnosticSeverity highest = results.Length == 0
            ? DiagnosticSeverity.Pass
            : DiagnosticSeverity.Pass;

        foreach (DiagnosticResult result in results)
        {
            switch (result.Status)
            {
                case DiagnosticStatus.Passed:
                    passed++;
                    break;
                case DiagnosticStatus.Warning:
                    warning++;
                    break;
                case DiagnosticStatus.Failed:
                    failed++;
                    break;
            }

            if (result.Severity > highest)
            {
                highest = result.Severity;
            }
        }

        return new DiagnosticSummary(
            TotalChecks: results.Length,
            PassedCount: passed,
            WarningCount: warning,
            FailedCount: failed,
            Healthy: failed == 0 && warning == 0,
            HighestSeverity: highest);
    }
}
