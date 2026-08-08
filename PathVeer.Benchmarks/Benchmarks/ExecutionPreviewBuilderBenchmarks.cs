using BenchmarkDotNet.Attributes;
using PathVeer.Benchmarks.Infrastructure;
using PathVeer.Core.Planning;
using PathVeer.Core.Runtime;

namespace PathVeer.Benchmarks.Benchmarks;

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

    // Repeated build of the same prebuilt decision; isolates steady-state
    // per-call allocation (no construction of inputs here).
    [Benchmark]
    public ExecutionPreview BuildTenTimes()
    {
        ExecutionPreview sink = _builder.Build(_decision);
        for (int i = 0; i < 9; i++)
        {
            sink = _builder.Build(_decision);
        }

        return sink;
    }

    // Read Categories once. Categories is cached on first access, so this
    // includes the one-time grouping cost.
    [Benchmark]
    public IReadOnlyDictionary<ExecutionPreviewCategory,
        IReadOnlyList<ExecutionPreviewStep>> ReadCategoriesOnce() =>
        _builder.Build(_decision).Categories;

    // Read Categories ten times; only the first access groups, the rest are
    // served from the cached dictionary (the dominant win of caching).
    [Benchmark]
    public IReadOnlyDictionary<ExecutionPreviewCategory,
        IReadOnlyList<ExecutionPreviewStep>> ReadCategoriesTenTimes()
    {
        ExecutionPreview preview = _builder.Build(_decision);
        IReadOnlyDictionary<ExecutionPreviewCategory,
            IReadOnlyList<ExecutionPreviewStep>> sink = preview.Categories;
        for (int i = 0; i < 9; i++)
        {
            sink = preview.Categories;
        }

        return sink;
    }
}
