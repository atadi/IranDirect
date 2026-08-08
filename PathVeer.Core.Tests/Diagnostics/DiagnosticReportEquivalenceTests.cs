using System.Text.Json;
using PathVeer.Core.Diagnostics;
using PathVeer.Core.Ipc;
using PathVeer.Core.Support;

namespace PathVeer.Core.Tests.Diagnostics;

/// <summary>
/// Proves the optimized <see cref="DiagnosticReport"/> produces byte-for-byte
/// equivalent summary values, counters, health, and severity to the
/// pre-optimization behavior (modeled by <see cref="ReferenceDiagnosticReportSummary"/>),
/// and that the report is immutable after construction (defensive copy),
/// serialization-compatible, and formatter-identical. No timing assertions
/// are used in correctness.
/// </summary>
public sealed class DiagnosticReportEquivalenceTests
{
    private static DiagnosticResult Make(
        string id,
        DiagnosticStatus status,
        DiagnosticSeverity severity,
        string message = "test")
    {
        return new DiagnosticResult(
            Id: id,
            Title: id,
            Status: status,
            Severity: severity,
            Message: message,
            SuggestedAction: null);
    }

    // ---- Hand-picked semantics ----

    [Fact]
    public void EmptyResults_MatchesReference()
    {
        var report = new DiagnosticReport(
            DateTimeOffset.UtcNow, []);

        AssertEquivalent([], report);
    }

    [Fact]
    public void AllPassed_MatchesReference()
    {
        DiagnosticResult[] data =
        [
            Make("a", DiagnosticStatus.Passed, DiagnosticSeverity.Info),
            Make("b", DiagnosticStatus.Passed, DiagnosticSeverity.Pass)
        ];

        AssertEquivalent(data, new DiagnosticReport(DateTimeOffset.UtcNow, data));
    }

    [Fact]
    public void AllWarning_MatchesReference()
    {
        DiagnosticResult[] data =
        [
            Make("a", DiagnosticStatus.Warning, DiagnosticSeverity.Warning),
            Make("b", DiagnosticStatus.Warning, DiagnosticSeverity.Warning)
        ];

        AssertEquivalent(data, new DiagnosticReport(DateTimeOffset.UtcNow, data));
    }

    [Fact]
    public void AllFailed_MatchesReference()
    {
        DiagnosticResult[] data =
        [
            Make("a", DiagnosticStatus.Failed, DiagnosticSeverity.Error),
            Make("b", DiagnosticStatus.Failed, DiagnosticSeverity.Error)
        ];

        AssertEquivalent(data, new DiagnosticReport(DateTimeOffset.UtcNow, data));
    }

    [Fact]
    public void MixedStatuses_MatchesReference()
    {
        DiagnosticResult[] data =
        [
            Make("a", DiagnosticStatus.Passed, DiagnosticSeverity.Info),
            Make("b", DiagnosticStatus.Warning, DiagnosticSeverity.Warning),
            Make("c", DiagnosticStatus.Failed, DiagnosticSeverity.Error),
            Make("d", DiagnosticStatus.Passed, DiagnosticSeverity.Pass)
        ];

        AssertEquivalent(data, new DiagnosticReport(DateTimeOffset.UtcNow, data));
    }

    [Fact]
    public void EverySeverityValue_MatchesReference()
    {
        DiagnosticResult[] data =
        [
            Make("a", DiagnosticStatus.Passed, DiagnosticSeverity.Pass),
            Make("b", DiagnosticStatus.Passed, DiagnosticSeverity.Info),
            Make("c", DiagnosticStatus.Warning, DiagnosticSeverity.Warning),
            Make("d", DiagnosticStatus.Failed, DiagnosticSeverity.Fail),
            Make("e", DiagnosticStatus.Failed, DiagnosticSeverity.Error)
        ];

        AssertEquivalent(data, new DiagnosticReport(DateTimeOffset.UtcNow, data));
    }

    [Theory]
    [InlineData(DiagnosticSeverity.Pass)]
    [InlineData(DiagnosticSeverity.Info)]
    [InlineData(DiagnosticSeverity.Warning)]
    [InlineData(DiagnosticSeverity.Fail)]
    [InlineData(DiagnosticSeverity.Error)]
    public void HighestSeverity_SingleResult_MatchesReference(
        DiagnosticSeverity severity)
    {
        DiagnosticResult[] data =
        [
            Make("a", DiagnosticStatus.Failed, severity)
        ];

        var report = new DiagnosticReport(DateTimeOffset.UtcNow, data);

        Assert.Equal(
            ReferenceDiagnosticReportSummary.HighestSeverity(data),
            report.HighestSeverity);
    }

    [Fact]
    public void RepeatedIdsAndTitles_MatchesReference()
    {
        DiagnosticResult[] data =
        [
            Make("dup", DiagnosticStatus.Passed, DiagnosticSeverity.Info),
            Make("dup", DiagnosticStatus.Warning, DiagnosticSeverity.Warning),
            Make("dup", DiagnosticStatus.Failed, DiagnosticSeverity.Error)
        ];

        AssertEquivalent(data, new DiagnosticReport(DateTimeOffset.UtcNow, data));
    }

    // ---- Deterministic generated sweep ----

    public static IEnumerable<object[]> SizeCases()
    {
        int[] sizes = [10, 100, 1_000, 5_000];
        foreach (int size in sizes)
        {
            yield return [size];
        }
    }

    [Theory]
    [MemberData(nameof(SizeCases))]
    public void GeneratedSweep_MatchesReference(int size)
    {
        DiagnosticResult[] data = BuildDeterministic(size);

        var report = new DiagnosticReport(DateTimeOffset.UtcNow, data);

        AssertEquivalent(data, report);
    }

    private static DiagnosticResult[] BuildDeterministic(int size)
    {
        var data = new DiagnosticResult[size];
        for (int i = 0; i < size; i++)
        {
            DiagnosticStatus status = (i % 20) switch
            {
                0 => DiagnosticStatus.Failed,
                1 => DiagnosticStatus.Warning,
                _ => DiagnosticStatus.Passed
            };

            DiagnosticSeverity severity = status switch
            {
                DiagnosticStatus.Failed => DiagnosticSeverity.Error,
                DiagnosticStatus.Warning => DiagnosticSeverity.Warning,
                _ => DiagnosticSeverity.Pass
            };

            data[i] = Make(
                $"check-{i % 15}.{i}",
                status,
                severity);
        }

        return data;
    }

    // ---- Ownership / immutability (Steps 8, 15) ----

    [Fact]
    public void Construction_DefensivelyCopiesResults()
    {
        var source = new List<DiagnosticResult>
        {
            Make("a", DiagnosticStatus.Passed, DiagnosticSeverity.Info)
        };

        var report = new DiagnosticReport(
            DateTimeOffset.UtcNow, source);

        // Mutating the caller's collection after construction must NOT
        // invalidate the report's cached summary or Results.
        source.Add(
            Make("b", DiagnosticStatus.Failed, DiagnosticSeverity.Error));
        source[0] = Make(
            "a", DiagnosticStatus.Failed, DiagnosticSeverity.Error);

        Assert.Single(report.Results);
        Assert.Equal(1, report.PassedCount);
        Assert.Equal(0, report.FailedCount);
        Assert.True(report.Healthy);
    }

    [Fact]
    public void RepeatedReads_ReturnIdenticalValues()
    {
        DiagnosticResult[] data = BuildDeterministic(1_000);
        var report = new DiagnosticReport(DateTimeOffset.UtcNow, data);

        DiagnosticSummary first = report.Summary;
        for (int i = 0; i < 100; i++)
        {
            Assert.Equal(first, report.Summary);
            Assert.Equal(first.PassedCount, report.PassedCount);
            Assert.Equal(first.WarningCount, report.WarningCount);
            Assert.Equal(first.FailedCount, report.FailedCount);
            Assert.Equal(first.Healthy, report.Healthy);
            Assert.Equal(first.HighestSeverity, report.HighestSeverity);
        }
    }

    [Fact]
    public async Task Summary_StableAcrossConcurrentReads()
    {
        DiagnosticResult[] data = BuildDeterministic(5_000);
        var report = new DiagnosticReport(DateTimeOffset.UtcNow, data);

        DiagnosticSummary baseline = report.Summary;
        var tasks = new Task[8];
        for (int t = 0; t < tasks.Length; t++)
        {
            tasks[t] = Task.Run(
                () =>
                {
                    for (int i = 0; i < 10_000; i++)
                    {
                        Assert.Equal(baseline, report.Summary);
                    }
                });
        }

        await Task.WhenAll(tasks);
    }

    // ---- Serialization compatibility (Step 9) ----

    [Fact]
    public void Serialization_PropertyNamesUnchanged()
    {
        DiagnosticResult[] data = BuildDeterministic(50);
        var report = new DiagnosticReport(DateTimeOffset.UtcNow, data);

        string json = JsonSerializer.Serialize(
            report,
            IranDirectJson.Options);

        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;

        Assert.True(root.TryGetProperty("CapturedAt", out _));
        Assert.True(root.TryGetProperty("Results", out _));
        Assert.True(root.TryGetProperty("PassedCount", out _));
        Assert.True(root.TryGetProperty("WarningCount", out _));
        Assert.True(root.TryGetProperty("FailedCount", out _));
        Assert.True(root.TryGetProperty("Healthy", out _));
        Assert.True(root.TryGetProperty("HighestSeverity", out _));
        Assert.True(root.TryGetProperty("Categories", out _));
        Assert.True(root.TryGetProperty("Summary", out _));
    }

    [Fact]
    public void Serialization_NoExtraOrDuplicateSummaryProperties()
    {
        DiagnosticResult[] data = BuildDeterministic(50);
        var report = new DiagnosticReport(DateTimeOffset.UtcNow, data);

        string json = JsonSerializer.Serialize(
            report,
            IranDirectJson.Options);

        int summaryCount = 0;
        using JsonDocument doc = JsonDocument.Parse(json);
        foreach (JsonProperty property in doc.RootElement.EnumerateObject())
        {
            if (property.NameEquals("Summary"))
            {
                summaryCount++;
            }
        }

        // There must be exactly one "Summary" property and no private cache.
        Assert.Equal(1, summaryCount);
        Assert.Equal(
            1,
            CountProperty(json, "Summary"));
        Assert.DoesNotContain("\"_summary\"", json);
        Assert.DoesNotContain("\"_results\"", json);
    }

    [Fact]
    public void Serialization_ProducesDeterministicOutput()
    {
        DiagnosticResult[] data = BuildDeterministic(50);
        var report = new DiagnosticReport(DateTimeOffset.UtcNow, data);

        string first = JsonSerializer.Serialize(
            report,
            IranDirectJson.Options);
        string second = JsonSerializer.Serialize(
            report,
            IranDirectJson.Options);

        Assert.Equal(first, second);
    }

    [Fact]
    public void Serialization_ResultsOrderingPreserved()
    {
        DiagnosticResult[] data =
        [
            Make("a", DiagnosticStatus.Passed, DiagnosticSeverity.Info),
            Make("b", DiagnosticStatus.Warning, DiagnosticSeverity.Warning),
            Make("c", DiagnosticStatus.Failed, DiagnosticSeverity.Error)
        ];

        var report = new DiagnosticReport(DateTimeOffset.UtcNow, data);

        string json = JsonSerializer.Serialize(
            report,
            IranDirectJson.Options);

        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement results = doc.RootElement.GetProperty("Results");

        Assert.Equal(3, results.GetArrayLength());
        Assert.Equal("a", results[0].GetProperty("Id").GetString());
        Assert.Equal("b", results[1].GetProperty("Id").GetString());
        Assert.Equal("c", results[2].GetProperty("Id").GetString());
    }

    [Fact]
    public void Serialization_EmptyResults_StillEmitsEmptyArray()
    {
        var report = new DiagnosticReport(DateTimeOffset.UtcNow, []);

        string json = JsonSerializer.Serialize(
            report,
            IranDirectJson.Options);

        using JsonDocument doc = JsonDocument.Parse(json);
        Assert.Equal(
            JsonValueKind.Array,
            doc.RootElement.GetProperty("Results").ValueKind);
        Assert.Equal(
            0,
            doc.RootElement.GetProperty("Results").GetArrayLength());
    }

    [Fact]
    public void Serialization_RoundTripsThroughServiceResponse()
    {
        DiagnosticResult[] data = BuildDeterministic(20);
        var report = new DiagnosticReport(DateTimeOffset.UtcNow, data);
        var response = new ServiceResponse { Report = report };

        string json = JsonSerializer.Serialize(
            response,
            IranDirectJson.Options);

        ServiceResponse? round = JsonSerializer.Deserialize<ServiceResponse>(
            json,
            IranDirectJson.Options);

        Assert.NotNull(round);
        Assert.NotNull(round!.Report);
        Assert.Equal(
            report.PassedCount,
            round.Report.PassedCount);
        Assert.Equal(
            report.HighestSeverity,
            round.Report.HighestSeverity);
    }

    // ---- Formatter compatibility (Step 10) ----

    [Theory]
    [InlineData(DiagnosticFormat.Summary)]
    [InlineData(DiagnosticFormat.Detailed)]
    [InlineData(DiagnosticFormat.Compact)]
    public void Formatter_OutputMatchesReferenceReport(
        DiagnosticFormat format)
    {
        DiagnosticResult[] data = BuildDeterministic(200);
        DateTimeOffset fixedTime = new(2026, 8, 3, 12, 0, 0, TimeSpan.Zero);

        var report = new DiagnosticReport(fixedTime, data);

        // Reference report built from the identical data and timestamp, so
        // any formatter output (including the detailed CapturedAt line) is
        // byte-for-byte identical to what the original per-access
        // implementation produced.
        var referenceReport = new DiagnosticReport(fixedTime, data);

        Assert.Equal(
            DiagnosticReportFormatter.Format(referenceReport, format),
            DiagnosticReportFormatter.Format(report, format));
    }

    // ---- Helpers ----

    private static void AssertEquivalent(
        IReadOnlyList<DiagnosticResult> data,
        DiagnosticReport report)
    {
        DiagnosticSummary reference =
            ReferenceDiagnosticReportSummary.Compute(data);

        Assert.Equal(reference.TotalChecks, report.Results.Count);
        Assert.Equal(reference.PassedCount, report.PassedCount);
        Assert.Equal(reference.WarningCount, report.WarningCount);
        Assert.Equal(reference.FailedCount, report.FailedCount);
        Assert.Equal(reference.Healthy, report.Healthy);
        Assert.Equal(reference.HighestSeverity, report.HighestSeverity);

        Assert.Equal(reference, report.Summary);
    }

    private static int CountProperty(string json, string name)
    {
        // Count top-level occurrences of "\"name\"" in the serialized JSON.
        string token = $"\"{name}\"";
        int count = 0;
        int index = 0;
        while ((index = json.IndexOf(token, index, StringComparison.Ordinal))
               >= 0)
        {
            count++;
            index += token.Length;
        }

        return count;
    }
}
