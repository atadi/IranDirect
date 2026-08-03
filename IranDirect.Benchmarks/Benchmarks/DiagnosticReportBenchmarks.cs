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
}
