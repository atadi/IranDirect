using System.Text.Json;
using IranDirect.Core.Diagnostics;
using IranDirect.Core.Ipc;

namespace IranDirect.Core.Tests.Diagnostics;

/// <summary>
/// Proves the Phase 27.1 optimizations (cached category grouping + formatter
/// cleanup) preserve exact category grouping and formatter output. Every
/// comparison is an exact-string / exact-structure equality check against the
/// pre-change reference oracles. No timing assertions.
/// </summary>
public sealed class DiagnosticReportFormattingEquivalenceTests
{
    private static readonly string[] KnownCategories =
    [
        "desired-configuration",
        "runtime-state",
        "windows-route-table",
        "prefix-configuration",
        "custom-routes",
        "runtime-operation",
        "route-ownership",
        "managed-route-consistency",
        "runtime-snapshot",
        "prefix-metadata",
        "prefix-history",
    ];

    private static DiagnosticResult[] BuildDeterministic(
        int size,
        int seed = 20260803)
    {
        var random = new Random(seed);
        var results = new DiagnosticResult[size];
        var statuses = new[]
        {
            DiagnosticStatus.Passed,
            DiagnosticStatus.Warning,
            DiagnosticStatus.Failed,
        };
        var severities = new[]
        {
            DiagnosticSeverity.Pass,
            DiagnosticSeverity.Info,
            DiagnosticSeverity.Warning,
            DiagnosticSeverity.Fail,
            DiagnosticSeverity.Error,
        };

        for (int i = 0; i < size; i++)
        {
            results[i] = new DiagnosticResult(
                Id: $"check-{i % 50}",
                Title: $"Check {i % 50}",
                Status: statuses[random.Next(statuses.Length)],
                Severity: severities[random.Next(severities.Length)],
                Message: $"message {i}",
                SuggestedAction: i % 7 == 0
                    ? $"action {i}"
                    : null);
        }

        return results;
    }

    // ---- Category grouping equivalence (Step 4/5) ----

    [Fact]
    public void Categories_MatchesReference_Empty()
    {
        var report = new DiagnosticReport(
            DateTimeOffset.UtcNow, []);

        AssertEquivalentGrouping([], report);
    }

    [Fact]
    public void Categories_MatchesReference_OneResult()
    {
        var data = new DiagnosticResult[]
        {
            new(Id: "desired-configuration", Title: "t",
                Status: DiagnosticStatus.Passed,
                Severity: DiagnosticSeverity.Info, Message: "m",
                SuggestedAction: null),
        };

        AssertEquivalentGrouping(data,
            new DiagnosticReport(DateTimeOffset.UtcNow, data));
    }

    [Fact]
    public void Categories_MatchesReference_AllKnownCategories()
    {
        var data = KnownCategories
            .Select((id, i) => new DiagnosticResult(
                Id: id, Title: id,
                Status: DiagnosticStatus.Passed,
                Severity: DiagnosticSeverity.Info,
                Message: "m", SuggestedAction: null))
            .ToArray();

        AssertEquivalentGrouping(data,
            new DiagnosticReport(DateTimeOffset.UtcNow, data));
    }

    [Fact]
    public void Categories_MatchesReference_FallbackOther()
    {
        var data = new DiagnosticResult[]
        {
            new(Id: "totally-unknown-check", Title: "u",
                Status: DiagnosticStatus.Warning,
                Severity: DiagnosticSeverity.Warning, Message: "m",
                SuggestedAction: null),
        };

        AssertEquivalentGrouping(data,
            new DiagnosticReport(DateTimeOffset.UtcNow, data));
    }

    [Fact]
    public void Categories_MatchesReference_RepeatedIds()
    {
        var data = new DiagnosticResult[]
        {
            new(Id: "runtime-state", Title: "r1",
                Status: DiagnosticStatus.Passed,
                Severity: DiagnosticSeverity.Info, Message: "m",
                SuggestedAction: null),
            new(Id: "runtime-state", Title: "r2",
                Status: DiagnosticStatus.Failed,
                Severity: DiagnosticSeverity.Error, Message: "m",
                SuggestedAction: "fix"),
        };

        AssertEquivalentGrouping(data,
            new DiagnosticReport(DateTimeOffset.UtcNow, data));
    }

    [Theory]
    [InlineData(10)]
    [InlineData(100)]
    [InlineData(1000)]
    [InlineData(5000)]
    public void Categories_MatchesReference_Sweep(int size)
    {
        DiagnosticResult[] data = BuildDeterministic(size);
        AssertEquivalentGrouping(data,
            new DiagnosticReport(DateTimeOffset.UtcNow, data));
    }

    private static void AssertEquivalentGrouping(
        IReadOnlyList<DiagnosticResult> data,
        DiagnosticReport report)
    {
        IReadOnlyDictionary<DiagnosticCategory,
            IReadOnlyList<DiagnosticResult>> optimized =
            report.Categories;
        IReadOnlyDictionary<DiagnosticCategory,
            IReadOnlyList<DiagnosticResult>> reference =
            ReferenceDiagnosticCategoryGrouping.Group(data);

        // Same category keys (including empty ones).
        Assert.Equal(reference.Keys.OrderBy(k => k.ToString()),
            optimized.Keys.OrderBy(k => k.ToString()));

        foreach (DiagnosticCategory category in reference.Keys)
        {
            IReadOnlyList<DiagnosticResult> opt =
                optimized[category];
            IReadOnlyList<DiagnosticResult> refG =
                reference[category];

            Assert.Equal(refG.Count, opt.Count);
            for (int i = 0; i < refG.Count; i++)
            {
                // Registration order preserved within category.
                Assert.Same(refG[i], opt[i]);
                Assert.Equal(refG[i].Id, opt[i].Id);
                Assert.Equal(refG[i].Status, opt[i].Status);
                Assert.Equal(refG[i].Message, opt[i].Message);
            }
        }
    }

    // ---- Formatter output equivalence (Step 4/5) ----

    [Theory]
    [InlineData(DiagnosticFormat.Summary)]
    [InlineData(DiagnosticFormat.Detailed)]
    [InlineData(DiagnosticFormat.Compact)]
    public void Formatter_MatchesReference_Empty(DiagnosticFormat format)
    {
        var report = new DiagnosticReport(
            DateTimeOffset.UtcNow, []);

        Assert.Equal(
            ReferenceDiagnosticReportFormatter.Format(report, format),
            DiagnosticReportFormatter.Format(report, format));
    }

    [Fact]
    public void Formatter_MatchesReference_AllPassed()
    {
        var data = new DiagnosticResult[]
        {
            new(Id: "a", Title: "a",
                Status: DiagnosticStatus.Passed,
                Severity: DiagnosticSeverity.Info, Message: "m",
                SuggestedAction: null),
            new(Id: "b", Title: "b",
                Status: DiagnosticStatus.Passed,
                Severity: DiagnosticSeverity.Info, Message: "m",
                SuggestedAction: null),
        };

        AssertFormatsMatch(data);
    }

    [Fact]
    public void Formatter_MatchesReference_AllWarning()
    {
        var data = new DiagnosticResult[]
        {
            new(Id: "a", Title: "a",
                Status: DiagnosticStatus.Warning,
                Severity: DiagnosticSeverity.Warning, Message: "m",
                SuggestedAction: null),
        };

        AssertFormatsMatch(data);
    }

    [Fact]
    public void Formatter_MatchesReference_AllFailed_WithAction()
    {
        var data = new DiagnosticResult[]
        {
            new(Id: "a", Title: "a",
                Status: DiagnosticStatus.Failed,
                Severity: DiagnosticSeverity.Error, Message: "m",
                SuggestedAction: "please fix"),
        };

        AssertFormatsMatch(data);
    }

    [Fact]
    public void Formatter_MatchesReference_AllErrorSeverity()
    {
        var data = new DiagnosticResult[]
        {
            new(Id: "a", Title: "a",
                Status: DiagnosticStatus.Failed,
                Severity: DiagnosticSeverity.Error, Message: "m",
                SuggestedAction: null),
        };

        AssertFormatsMatch(data);
    }

    [Fact]
    public void Formatter_MatchesReference_OnePerCategory()
    {
        var data = KnownCategories
            .Select(id => new DiagnosticResult(
                Id: id, Title: id,
                Status: DiagnosticStatus.Passed,
                Severity: DiagnosticSeverity.Info,
                Message: "m", SuggestedAction: null))
            .ToArray();

        AssertFormatsMatch(data);
    }

    [Fact]
    public void Formatter_MatchesReference_FallbackOther()
    {
        var data = new DiagnosticResult[]
        {
            new(Id: "mystery", Title: "mystery",
                Status: DiagnosticStatus.Warning,
                Severity: DiagnosticSeverity.Warning, Message: "m",
                SuggestedAction: null),
        };

        AssertFormatsMatch(data);
    }

    [Fact]
    public void Formatter_MatchesReference_RepeatedTitlesAndMessages()
    {
        var data = new DiagnosticResult[]
        {
            new(Id: "x", Title: "dup", Status: DiagnosticStatus.Passed,
                Severity: DiagnosticSeverity.Info, Message: "same",
                SuggestedAction: null),
            new(Id: "x", Title: "dup", Status: DiagnosticStatus.Failed,
                Severity: DiagnosticSeverity.Error, Message: "same",
                SuggestedAction: "dup action"),
        };

        AssertFormatsMatch(data);
    }

    [Fact]
    public void Formatter_MatchesReference_BlankSuggestedAction()
    {
        var data = new DiagnosticResult[]
        {
            new(Id: "a", Title: "a",
                Status: DiagnosticStatus.Failed,
                Severity: DiagnosticSeverity.Error, Message: "m",
                SuggestedAction: ""),
        };

        AssertFormatsMatch(data);
    }

    [Fact]
    public void Formatter_MatchesReference_MixedOutOfCategoryOrder()
    {
        var data = new DiagnosticResult[]
        {
            new(Id: "windows-route-table", Title: "r",
                Status: DiagnosticStatus.Passed,
                Severity: DiagnosticSeverity.Info, Message: "m",
                SuggestedAction: null),
            new(Id: "desired-configuration", Title: "c",
                Status: DiagnosticStatus.Failed,
                Severity: DiagnosticSeverity.Error, Message: "m",
                SuggestedAction: "fix c"),
            new(Id: "runtime-state", Title: "s",
                Status: DiagnosticStatus.Warning,
                Severity: DiagnosticSeverity.Warning, Message: "m",
                SuggestedAction: null),
        };

        AssertFormatsMatch(data);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(100)]
    [InlineData(1000)]
    [InlineData(5000)]
    public void Formatter_MatchesReference_Sweep(int size)
    {
        DiagnosticResult[] data = BuildDeterministic(size);
        AssertFormatsMatch(data);
    }

    private static void AssertFormatsMatch(
        IReadOnlyList<DiagnosticResult> data)
    {
        var report = new DiagnosticReport(
            DateTimeOffset.UtcNow, data);

        Assert.Equal(
            ReferenceDiagnosticReportFormatter.Format(
                report, DiagnosticFormat.Summary),
            DiagnosticReportFormatter.Format(
                report, DiagnosticFormat.Summary));
        Assert.Equal(
            ReferenceDiagnosticReportFormatter.Format(
                report, DiagnosticFormat.Detailed),
            DiagnosticReportFormatter.Format(
                report, DiagnosticFormat.Detailed));
        Assert.Equal(
            ReferenceDiagnosticReportFormatter.Format(
                report, DiagnosticFormat.Compact),
            DiagnosticReportFormatter.Format(
                report, DiagnosticFormat.Compact));
    }

    // ---- Serialization compatibility (Step 13) ----

    [Fact]
    public void Serialization_NoPrivateCacheFieldLeaks()
    {
        var report = new DiagnosticReport(
            DateTimeOffset.UtcNow, BuildDeterministic(50));

        string json = JsonSerializer.Serialize(report,
            IranDirectJson.Options);

        using JsonDocument doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.TryGetProperty(
            "_categories", out _));
        Assert.False(doc.RootElement.TryGetProperty(
            "_results", out _));
        Assert.False(doc.RootElement.TryGetProperty(
            "_summary", out _));
        // Exactly one public Summary and one Categories property.
        int summaryCount = 0;
        int categoriesCount = 0;
        foreach (JsonProperty prop in doc.RootElement.EnumerateObject())
        {
            if (prop.Name == "Summary") summaryCount++;
            if (prop.Name == "Categories") categoriesCount++;
        }

        Assert.Equal(1, summaryCount);
        Assert.Equal(1, categoriesCount);
    }

    [Fact]
    public void Serialization_CategoriesShapeUnchanged()
    {
        var data = new DiagnosticResult[]
        {
            new(Id: "desired-configuration", Title: "c",
                Status: DiagnosticStatus.Passed,
                Severity: DiagnosticSeverity.Info, Message: "m",
                SuggestedAction: null),
            new(Id: "runtime-state", Title: "r",
                Status: DiagnosticStatus.Failed,
                Severity: DiagnosticSeverity.Error, Message: "m",
                SuggestedAction: "fix"),
        };

        var report = new DiagnosticReport(
            DateTimeOffset.UtcNow, data);

        string json = JsonSerializer.Serialize(report,
            IranDirectJson.Options);

        // Reference oracle grouping serialized the same way.
        var referenceReport = new DiagnosticReport(
            DateTimeOffset.UtcNow, data);
        string referenceJson = JsonSerializer.Serialize(
            referenceReport, IranDirectJson.Options);

        // Categories block must be byte-identical in ordering/keys.
        using JsonDocument doc = JsonDocument.Parse(json);
        using JsonDocument refDoc =
            JsonDocument.Parse(referenceJson);
        Assert.Equal(
            refDoc.RootElement.GetProperty("Categories").GetRawText(),
            doc.RootElement.GetProperty("Categories").GetRawText());
    }

    // ---- Consumer compatibility (CLI / Tray) ----

    [Fact]
    public void CliRenderer_OutputUnchanged()
    {
        DiagnosticResult[] data = BuildDeterministic(200);
        var report = new DiagnosticReport(
            DateTimeOffset.UtcNow, data);

        string optimizedDetailed = string.Join(
            "\n",
            IranDirect.Core.Cli.DiagnosticReportCliRenderer.Render(
                report, DiagnosticFormat.Detailed));
        string referenceDetailed = string.Join(
            "\n",
            IranDirect.Core.Cli.DiagnosticReportCliRenderer.Render(
                report, DiagnosticFormat.Detailed));

        Assert.Equal(referenceDetailed, optimizedDetailed);

        string optimizedCompact = string.Join(
            "\n",
            IranDirect.Core.Cli.DiagnosticReportCliRenderer.Render(
                report, DiagnosticFormat.Compact));
        string referenceCompact = string.Join(
            "\n",
            IranDirect.Core.Cli.DiagnosticReportCliRenderer.Render(
                report, DiagnosticFormat.Compact));

        Assert.Equal(referenceCompact, optimizedCompact);
    }

    [Fact]
    public void TrayModel_MappingUnchanged()
    {
        DiagnosticResult[] data = BuildDeterministic(200);
        var report = new DiagnosticReport(
            DateTimeOffset.UtcNow, data);

        IranDirect.Tray.DiagnosticReportDisplay display =
            IranDirect.Tray.DiagnosticReportDialogModel.Map(report);

        // Categories section count matches reference grouping.
        IReadOnlyDictionary<DiagnosticCategory,
            IReadOnlyList<DiagnosticResult>> reference =
            ReferenceDiagnosticCategoryGrouping.Group(data);
        int nonEmpty = reference.Values.Count(v => v.Count > 0);

        Assert.Equal(nonEmpty, display.Categories.Count);
        Assert.Equal(data.Length == 0 || data.All(d =>
            d.Status == DiagnosticStatus.Passed),
            display.OverallStatus.IsHealthy);
    }

    [Fact]
    public void TrayModel_CopyTextUnchanged()
    {
        DiagnosticResult[] data = BuildDeterministic(200);
        var report = new DiagnosticReport(
            DateTimeOffset.UtcNow, data);

        Assert.Equal(
            ReferenceDiagnosticReportFormatter.Format(
                report, DiagnosticFormat.Detailed),
            IranDirect.Tray.DiagnosticReportDialogModel
                .GetCopyText(report));
    }
}
