using PathVeer.Core.Diagnostics;

namespace PathVeer.Core.Tests.Diagnostics;

/// <summary>
/// Test-only reproduction of the pre-optimization <see cref="DiagnosticReport"/>
/// summary logic. Each property mirrors the original implementation exactly
/// (per-status <c>Count</c> scans, the highest-severity loop, the
/// <c>Healthy</c> derivation, and the <see cref="DiagnosticSummary"/>
/// projection) so equivalence tests can compare the optimized report against
/// an independent, behavior-faithful oracle. This type must never ship in
/// production code — it exists only to pin observable semantics during the
/// allocation optimization.
/// </summary>
public static class ReferenceDiagnosticReportSummary
{
    public static DiagnosticSummary Compute(IReadOnlyList<DiagnosticResult> results)
    {
        int passed = results.Count(r => r.Status == DiagnosticStatus.Passed);
        int warning = results.Count(r => r.Status == DiagnosticStatus.Warning);
        int failed = results.Count(r => r.Status == DiagnosticStatus.Failed);

        DiagnosticSeverity highest =
            results.Count == 0
                ? DiagnosticSeverity.Pass
                : DiagnosticSeverity.Pass;

        foreach (DiagnosticResult result in results)
        {
            if (result.Severity > highest)
            {
                highest = result.Severity;
            }
        }

        return new DiagnosticSummary(
            TotalChecks: results.Count,
            PassedCount: passed,
            WarningCount: warning,
            FailedCount: failed,
            Healthy: failed == 0 && warning == 0,
            HighestSeverity: highest);
    }

    public static int PassedCount(IReadOnlyList<DiagnosticResult> results) =>
        results.Count(r => r.Status == DiagnosticStatus.Passed);

    public static int WarningCount(IReadOnlyList<DiagnosticResult> results) =>
        results.Count(r => r.Status == DiagnosticStatus.Warning);

    public static int FailedCount(IReadOnlyList<DiagnosticResult> results) =>
        results.Count(r => r.Status == DiagnosticStatus.Failed);

    public static bool Healthy(IReadOnlyList<DiagnosticResult> results)
    {
        int warning = results.Count(r => r.Status == DiagnosticStatus.Warning);
        int failed = results.Count(r => r.Status == DiagnosticStatus.Failed);
        return failed == 0 && warning == 0;
    }

    public static DiagnosticSeverity HighestSeverity(
        IReadOnlyList<DiagnosticResult> results)
    {
        if (results.Count == 0)
        {
            return DiagnosticSeverity.Pass;
        }

        DiagnosticSeverity highest = DiagnosticSeverity.Pass;
        foreach (DiagnosticResult result in results)
        {
            if (result.Severity > highest)
            {
                highest = result.Severity;
            }
        }

        return highest;
    }
}
