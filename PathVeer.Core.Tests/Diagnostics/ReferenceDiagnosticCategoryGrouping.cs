using System.Collections.ObjectModel;
using PathVeer.Core.Diagnostics;

namespace PathVeer.Core.Tests.Diagnostics;

/// <summary>
/// Test-only reference reproduction of the pre-change
/// <see cref="DiagnosticCategoryMap.GroupResults"/> behavior. Mirrors the
/// original implementation exactly: two dictionaries, six empty lists seeded
/// in enum order, one result pass, then a wrapping dictionary of
/// <see cref="ReadOnlyCollection{T}"/> values. Does not call the optimized
/// grouping path.
/// </summary>
public static class ReferenceDiagnosticCategoryGrouping
{
    public static IReadOnlyDictionary<DiagnosticCategory,
        IReadOnlyList<DiagnosticResult>> Group(
        IReadOnlyList<DiagnosticResult> results)
    {
        var groups =
            new Dictionary<DiagnosticCategory,
                List<DiagnosticResult>>();

        foreach (DiagnosticCategory cat in
            Enum.GetValues<DiagnosticCategory>())
        {
            groups[cat] = [];
        }

        foreach (DiagnosticResult result in results)
        {
            DiagnosticCategory category =
                DiagnosticCategoryMap.Default.GetCategory(result.Id);
            groups[category].Add(result);
        }

        var ordered =
            new Dictionary<DiagnosticCategory,
                IReadOnlyList<DiagnosticResult>>();

        foreach (var kvp in groups)
        {
            ordered[kvp.Key] =
                new ReadOnlyCollection<DiagnosticResult>(
                    kvp.Value);
        }

        return ordered;
    }
}
