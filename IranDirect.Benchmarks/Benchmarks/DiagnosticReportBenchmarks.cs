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
}
