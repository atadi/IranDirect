using BenchmarkDotNet.Attributes;
using PathVeer.Benchmarks.Infrastructure;
using PathVeer.Core.Runtime;
using PathVeer.Core.Runtime.Reconciliation;
using PathVeer.Testing.Performance.Workloads;

namespace PathVeer.Benchmarks.Benchmarks;

[MemoryDiagnoser]
public class RuntimeChangeSetPlannerBenchmarks
{
    private readonly RuntimeChangeSetPlanner _planner = new();

    private RuntimePlanSnapshot _snapshot = null!;
    private RuntimeRouteOwnership _ownership = null!;

    [ParamsSource(nameof(ScenarioValues))]
    public RuntimeWorkloadScenario Scenario;

    [ParamsSource(nameof(SizeValues))]
    public int Size;

    public static IEnumerable<RuntimeWorkloadScenario> ScenarioValues() =>
        new[]
        {
            RuntimeWorkloadScenario.AllMissing,
            RuntimeWorkloadScenario.AllPresent,
            RuntimeWorkloadScenario.AllObsolete,
            RuntimeWorkloadScenario.Mixed,
            RuntimeWorkloadScenario.DuplicateInput
        };

    public static IEnumerable<int> SizeValues() =>
        Sizes.DatasetSizes;

    [GlobalSetup]
    public void Setup()
    {
        RuntimeWorkload workload =
            new RuntimeWorkloadGenerator().Generate(
                Scenario,
                (RuntimeWorkloadSize)Size);

        PlannerInput input =
            RuntimeSnapshotFactory.CreatePlannerInput(workload);
        _snapshot = input.Snapshot;
        _ownership = input.Ownership;
    }

    [Benchmark]
    public RuntimeChangeSet Plan() =>
        _planner.Plan(_snapshot, _ownership);
}
