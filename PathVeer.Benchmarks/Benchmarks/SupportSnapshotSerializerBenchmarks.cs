using BenchmarkDotNet.Attributes;
using PathVeer.Benchmarks.Infrastructure;
using PathVeer.Core.Support;

namespace PathVeer.Benchmarks.Benchmarks;

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

    // Repeated serialization of a large snapshot. This isolates steady-state
    // per-call allocation/throughput where cached serialization metadata
    // (reflection or source-generated) is fully warm.
    [Benchmark]
    public string SerializeLargeTenTimes()
    {
        string sink = string.Empty;
        for (int i = 0; i < 10; i++)
        {
            sink = _serializer.Serialize(_snapshot);
        }

        return sink;
    }

    // Direct UTF-8 byte serialization (the export path). This avoids the
    // intermediate string and the exporter's string -> UTF-8 re-encode, so its
    // allocation is ~1x payload instead of ~2x payload (string + bytes).
    [Benchmark]
    public byte[] SerializeToUtf8Bytes() =>
        _serializer.SerializeToUtf8Bytes(_snapshot);
}
