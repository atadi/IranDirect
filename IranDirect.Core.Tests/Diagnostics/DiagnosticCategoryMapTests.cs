using IranDirect.Core.Diagnostics;

namespace IranDirect.Core.Tests.Diagnostics;

public sealed class DiagnosticCategoryMapTests
{
    [Fact]
    public void DefaultMap_ConfigurationCheck_ReturnsConfiguration()
    {
        DiagnosticCategory category =
            DiagnosticCategoryMap.Default.GetCategory(
                "desired-configuration");

        Assert.Equal(
            DiagnosticCategory.Configuration, category);
    }

    [Fact]
    public void DefaultMap_RuntimeCheck_ReturnsRuntime()
    {
        DiagnosticCategory category =
            DiagnosticCategoryMap.Default.GetCategory(
                "runtime-state");

        Assert.Equal(DiagnosticCategory.Runtime, category);
    }

    [Fact]
    public void DefaultMap_RoutingCheck_ReturnsRouting()
    {
        DiagnosticCategory category =
            DiagnosticCategoryMap.Default.GetCategory(
                "windows-route-table");

        Assert.Equal(DiagnosticCategory.Routing, category);
    }

    [Fact]
    public void DefaultMap_UnknownId_ReturnsConfiguration()
    {
        DiagnosticCategory category =
            DiagnosticCategoryMap.Default.GetCategory(
                "some-unknown-check");

        Assert.Equal(
            DiagnosticCategory.Configuration, category);
    }

    [Fact]
    public void DefaultMap_CaseInsensitive()
    {
        DiagnosticCategory category =
            DiagnosticCategoryMap.Default.GetCategory(
                "RUNTIME-STATE");

        Assert.Equal(DiagnosticCategory.Runtime, category);
    }

    [Fact]
    public void CustomMap_OverridesDefault()
    {
        var map = new Dictionary<string, DiagnosticCategory>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["my-check"] = DiagnosticCategory.Vpn
        };

        var categoryMap = new DiagnosticCategoryMap(map);

        DiagnosticCategory category =
            categoryMap.GetCategory("my-check");

        Assert.Equal(DiagnosticCategory.Vpn, category);
    }

    [Fact]
    public void CustomMap_UnknownFallsBackToConfiguration()
    {
        var map = new Dictionary<string, DiagnosticCategory>(
            StringComparer.OrdinalIgnoreCase);

        var categoryMap = new DiagnosticCategoryMap(map);

        DiagnosticCategory category =
            categoryMap.GetCategory("unknown");

        Assert.Equal(
            DiagnosticCategory.Configuration, category);
    }

    [Fact]
    public void GroupResults_EmptyResults_AllCategoriesEmpty()
    {
        IReadOnlyDictionary<DiagnosticCategory,
            IReadOnlyList<DiagnosticResult>> groups =
            DiagnosticCategoryMap.Default.GroupResults([]);

        foreach (DiagnosticCategory category in
            Enum.GetValues<DiagnosticCategory>())
        {
            Assert.Empty(groups[category]);
        }
    }

    [Fact]
    public void GroupResults_MultipleResults_GroupedCorrectly()
    {
        var results = new List<DiagnosticResult>
        {
            new(
                Id: "desired-configuration",
                Title: "Desired configuration",
                Status: DiagnosticStatus.Passed,
                Severity: DiagnosticSeverity.Info,
                Message: "ok",
                SuggestedAction: null),
            new(
                Id: "runtime-state",
                Title: "Runtime state",
                Status: DiagnosticStatus.Passed,
                Severity: DiagnosticSeverity.Info,
                Message: "ok",
                SuggestedAction: null),
            new(
                Id: "runtime-snapshot",
                Title: "Runtime snapshot",
                Status: DiagnosticStatus.Warning,
                Severity: DiagnosticSeverity.Warning,
                Message: "warn",
                SuggestedAction: null),
            new(
                Id: "windows-route-table",
                Title: "Windows route table",
                Status: DiagnosticStatus.Passed,
                Severity: DiagnosticSeverity.Info,
                Message: "ok",
                SuggestedAction: null),
        };

        IReadOnlyDictionary<DiagnosticCategory,
            IReadOnlyList<DiagnosticResult>> groups =
            DiagnosticCategoryMap.Default.GroupResults(
                results);

        Assert.Single(
            groups[DiagnosticCategory.Configuration]);
        Assert.Equal(
            2,
            groups[DiagnosticCategory.Runtime].Count);
        Assert.Single(
            groups[DiagnosticCategory.Routing]);
    }

    [Fact]
    public void GroupResults_UnknownId_GoesToConfiguration()
    {
        var results = new List<DiagnosticResult>
        {
            new(
                Id: "totally-unknown",
                Title: "Unknown",
                Status: DiagnosticStatus.Passed,
                Severity: DiagnosticSeverity.Info,
                Message: "ok",
                SuggestedAction: null),
        };

        IReadOnlyDictionary<DiagnosticCategory,
            IReadOnlyList<DiagnosticResult>> groups =
            DiagnosticCategoryMap.Default.GroupResults(
                results);

        Assert.Single(
            groups[DiagnosticCategory.Configuration]);
    }

    [Fact]
    public void GroupResults_CreatesEmptyListsForEachCategory()
    {
        IReadOnlyDictionary<DiagnosticCategory,
            IReadOnlyList<DiagnosticResult>> groups =
            DiagnosticCategoryMap.Default.GroupResults([]);

        // Every category should have a key
        Assert.True(
            groups.ContainsKey(DiagnosticCategory.Configuration));
        Assert.True(
            groups.ContainsKey(DiagnosticCategory.Runtime));
        Assert.True(
            groups.ContainsKey(DiagnosticCategory.Routing));
        Assert.True(
            groups.ContainsKey(DiagnosticCategory.Vpn));
        Assert.True(
            groups.ContainsKey(DiagnosticCategory.Prefixes));
        Assert.True(
            groups.ContainsKey(DiagnosticCategory.Updates));
    }
}
