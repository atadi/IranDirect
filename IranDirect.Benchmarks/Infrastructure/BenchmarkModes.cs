using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Loggers;

namespace IranDirect.Benchmarks.Infrastructure;

public static class BenchmarkModes
{
    public const string QuickFlag = "--quick";

    public static bool IsQuickFlag(string argument) =>
        string.Equals(
            argument,
            QuickFlag,
            StringComparison.OrdinalIgnoreCase);

    public static IConfig Create(bool quick) =>
        ManualConfig.CreateEmpty()
            .AddLogger(ConsoleLogger.Default)
            .AddColumnProvider(DefaultColumnProviders.Instance)
            .AddExporter(
                DefaultExporters.Markdown,
                DefaultExporters.Csv,
                DefaultExporters.CsvMeasurements,
                DefaultExporters.Html)
            .AddJob(quick ? QuickJob() : FullJob())
            .AddDiagnoser(MemoryDiagnoser.Default)
            .WithArtifactsPath(ArtifactsPath)
            .WithOptions(ConfigOptions.DisableOptimizationsValidator);

    private static Job QuickJob() =>
        Job.Default
            .WithLaunchCount(1)
            .WithWarmupCount(2)
            .WithIterationCount(3)
            .WithStrategy(RunStrategy.Throughput);

    private static Job FullJob() =>
        Job.Default
            .WithLaunchCount(1)
            .WithWarmupCount(5)
            .WithIterationCount(7)
            .WithStrategy(RunStrategy.Throughput);

    private static string ArtifactsPath
    {
        get
        {
            string assemblyDirectory =
                Path.GetDirectoryName(typeof(Program).Assembly.Location)!;
            string projectRoot = Path.GetFullPath(
                Path.Combine(
                    assemblyDirectory,
                    "..",
                    "..",
                    "..",
                    ".."));
            return Path.Combine(
                projectRoot,
                "BenchmarkDotNet.Artifacts");
        }
    }
}
