using BenchmarkDotNet.Attributes;
using IranDirect.Benchmarks.Infrastructure;
using IranDirect.Core.Support;

namespace IranDirect.Benchmarks.Benchmarks;

[MemoryDiagnoser]
public class SupportSnapshotSerializerBenchmarks
{
    private readonly SupportSnapshotSerializer _serializer = new();

    private SupportSnapshot _snapshot = null!;

    [ParamsAllValues]
    public SnapshotSize Size;

    [GlobalSetup]
    public void Setup()
    {
        _snapshot = SupportSnapshotFactory.Create(Size);
    }

    [Benchmark]
    public string Serialize() =>
        _serializer.Serialize(_snapshot);
}
