using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using IranDirect.Benchmarks.Infrastructure;

namespace IranDirect.Benchmarks;

public static class Program
{
    public static int Main(string[] args)
    {
        bool quick = args.Any(BenchmarkModes.IsQuickFlag);

        string[] benchmarkArgs = args
            .Where(argument => !BenchmarkModes.IsQuickFlag(argument))
            .ToArray();

        IConfig config = BenchmarkModes.Create(quick);

        IEnumerable<Summary> summaries = BenchmarkSwitcher
            .FromAssembly(typeof(Program).Assembly)
            .Run(benchmarkArgs, config);

        return summaries.Any(summary => summary.HasCriticalValidationErrors)
            ? 1
            : 0;
    }
}
