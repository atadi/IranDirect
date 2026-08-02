using System.Collections.ObjectModel;

namespace IranDirect.Core.Diagnostics;

public sealed class DiagnosticCategoryMap
{
    private readonly IReadOnlyDictionary<string, DiagnosticCategory>
        _map;

    public static DiagnosticCategoryMap Default { get; } =
        CreateDefault();

    public DiagnosticCategoryMap(
        IReadOnlyDictionary<string, DiagnosticCategory> map)
    {
        _map = map;
    }

    public DiagnosticCategory GetCategory(string checkId)
    {
        if (_map.TryGetValue(
                checkId,
                out DiagnosticCategory category))
        {
            return category;
        }

        return DiagnosticCategory.Configuration;
    }

    public IReadOnlyDictionary<DiagnosticCategory,
        IReadOnlyList<DiagnosticResult>> GroupResults(
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
                GetCategory(result.Id);
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

    private static DiagnosticCategoryMap CreateDefault()
    {
        var map =
            new Dictionary<string, DiagnosticCategory>(
                StringComparer.OrdinalIgnoreCase)
            {
                ["desired-configuration"] =
                    DiagnosticCategory.Configuration,
                ["prefix-configuration"] =
                    DiagnosticCategory.Configuration,
                ["prefix-metadata"] =
                    DiagnosticCategory.Configuration,
                ["prefix-history"] =
                    DiagnosticCategory.Configuration,
                ["custom-routes"] =
                    DiagnosticCategory.Configuration,
                ["runtime-state"] =
                    DiagnosticCategory.Runtime,
                ["runtime-snapshot"] =
                    DiagnosticCategory.Runtime,
                ["runtime-operation"] =
                    DiagnosticCategory.Runtime,
                ["route-inventory"] =
                    DiagnosticCategory.Runtime,
                ["windows-route-table"] =
                    DiagnosticCategory.Routing,
                ["route-ownership"] =
                    DiagnosticCategory.Routing,
                ["managed-route-consistency"] =
                    DiagnosticCategory.Routing,
            };

        return new DiagnosticCategoryMap(map);
    }
}
