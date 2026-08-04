using BenchmarkDotNet.Attributes;
using IranDirect.Benchmarks.Infrastructure;
using IranDirect.Core.Diagnostics;

namespace IranDirect.Benchmarks.Benchmarks;

[MemoryDiagnoser]
public class DiagnosticReportBenchmarks
{
    private DiagnosticReport _report = null!;

    [ParamsSource(nameof(ResultCountValues))]
    public int ResultCount;

    public static IEnumerable<int> ResultCountValues() =>
        Sizes.DiagnosticResultCounts;

    [GlobalSetup]
    public void Setup()
    {
        _report = DiagnosticReportFactory.Create(ResultCount);
    }

    [Benchmark]
    public DiagnosticSummary ComputedSummaryAccess() =>
        _report.Summary;

    [Benchmark]
    public IReadOnlyDictionary<
        DiagnosticCategory,
        IReadOnlyList<DiagnosticResult>> CategoryGrouping() =>
        _report.Categories;

    [Benchmark]
    public string DetailedFormatting() =>
        DiagnosticReportFormatter.Format(
            _report,
            DiagnosticFormat.Detailed);

    [Benchmark]
    public string CompactFormatting() =>
        DiagnosticReportFormatter.Format(
            _report,
            DiagnosticFormat.Compact);

    // ---- Focused repeated-access isolation (Phase 26.1) ----

    [Benchmark]
    public DiagnosticSummary ReadSummaryOnce() => _report.Summary;

    [Benchmark]
    public int ReadSummaryTenTimes()
    {
        int sink = 0;
        for (int i = 0; i < 10; i++)
        {
            sink += _report.Summary.TotalChecks;
        }

        return sink;
    }

    [Benchmark]
    public int ReadSummaryOneHundredTimes()
    {
        int sink = 0;
        for (int i = 0; i < 100; i++)
        {
            sink += _report.Summary.TotalChecks;
        }

        return sink;
    }

    [Benchmark]
    public int ReadIndividualCountersOnce()
    {
        int sink =
            _report.PassedCount +
            _report.WarningCount +
            _report.FailedCount;
        return sink;
    }

    [Benchmark]
    public int ReadIndividualCountersTenTimes()
    {
        int sink = 0;
        for (int i = 0; i < 10; i++)
        {
            sink +=
                _report.PassedCount +
                _report.WarningCount +
                _report.FailedCount;
        }

        return sink;
    }

    [Benchmark]
    public DiagnosticSeverity ReadHighestSeverityTenTimes()
    {
        DiagnosticSeverity sink = DiagnosticSeverity.Pass;
        for (int i = 0; i < 10; i++)
        {
            sink = _report.HighestSeverity;
        }

        return sink;
    }

    [Benchmark]
    public bool ReadHealthyTenTimes()
    {
        bool sink = true;
        for (int i = 0; i < 10; i++)
        {
            sink = _report.Healthy;
        }

        return sink;
    }

    // ---- Focused repeated-access isolation (Phase 27.1) ----

    [Benchmark]
    public IReadOnlyDictionary<
        DiagnosticCategory,
        IReadOnlyList<DiagnosticResult>> CategoriesTenTimes()
    {
        IReadOnlyDictionary<DiagnosticCategory,
            IReadOnlyList<DiagnosticResult>> sink =
            _report.Categories;
        for (int i = 0; i < 10; i++)
        {
            sink = _report.Categories;
        }

        return sink;
    }

    [Benchmark]
    public int DetailedFormattingTenTimes()
    {
        int sink = 0;
        for (int i = 0; i < 10; i++)
        {
            string s = DiagnosticReportFormatter.Format(
                _report, DiagnosticFormat.Detailed);
            sink += s.Length;
        }

        return sink;
    }

    [Benchmark]
    public int CompactFormattingTenTimes()
    {
        int sink = 0;
        for (int i = 0; i < 10; i++)
        {
            string s = DiagnosticReportFormatter.Format(
                _report, DiagnosticFormat.Compact);
            sink += s.Length;
        }

        return sink;
    }
}

/// <summary>
/// Measures the cost of constructing a <see cref="DiagnosticReport"/>, which
/// now performs: defensive copy of Results, one-time summary computation, and
/// one-time category grouping. Input generation stays in GlobalSetup; the
/// measured method only executes the constructor.
/// </summary>
[MemoryDiagnoser]
public class DiagnosticReportConstructionBenchmarks
{
    private List<DiagnosticResult> _input = null!;

    [ParamsSource(nameof(ResultCountValues))]
    public int ResultCount;

    public static IEnumerable<int> ResultCountValues() =>
        Sizes.DiagnosticResultCounts;

    [GlobalSetup]
    public void Setup()
    {
        // Same deterministic generation as DiagnosticReportFactory but without
        // constructing the report here (input only).
        _input = DiagnosticReportFactory.Create(ResultCount).Results.ToList();
    }

    [Benchmark]
    public DiagnosticReport ReportConstruction() =>
        new DiagnosticReport(
            CapturedAt: FixedTime.Value,
            Results: _input);
}
