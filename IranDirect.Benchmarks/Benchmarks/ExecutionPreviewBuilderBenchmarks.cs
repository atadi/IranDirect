using BenchmarkDotNet.Attributes;
using IranDirect.Benchmarks.Infrastructure;
using IranDirect.Core.Planning;
using IranDirect.Core.Runtime;

namespace IranDirect.Benchmarks.Benchmarks;

[MemoryDiagnoser]
public class ExecutionPreviewBuilderBenchmarks
{
    private readonly ExecutionPreviewBuilder _builder =
        new(TimeProvider.System);

    private RuntimeDecision _decision = null!;

    [ParamsSource(nameof(StepCountValues))]
    public int StepCount;

    public static IEnumerable<int> StepCountValues() =>
        Sizes.PreviewStepCounts;

    [GlobalSetup]
    public void Setup()
    {
        _decision = RuntimeSnapshotFactory.CreateDecision(StepCount);
    }

    [Benchmark]
    public ExecutionPreview Build() =>
        _builder.Build(_decision);
}
