using BenchmarkDotNet.Attributes;
using PathVeer.Benchmarks.Infrastructure;
using PathVeer.Core.Prefixes;

namespace PathVeer.Benchmarks.Benchmarks;

[MemoryDiagnoser]
public class PrefixDatasetComparerBenchmarks
{
    private IReadOnlyList<string> _oldDataset = [];
    private IReadOnlyList<string> _newDataset = [];

    [ParamsAllValues]
    public DatasetScenario Scenario;

    [ParamsSource(nameof(SizeValues))]
    public int Size;

    public static IEnumerable<int> SizeValues() =>
        Sizes.DatasetSizes;

    [GlobalSetup]
    public void Setup()
    {
        PrefixDatasetPair pair = PrefixDatasetFactory.Create(
            Scenario,
            Size);
        _oldDataset = pair.OldDataset;
        _newDataset = pair.NewDataset;
    }

    [Benchmark]
    public PrefixDatasetDiff Compare() =>
        PrefixDatasetComparer.Compare(
            _oldDataset,
            _newDataset);
}
