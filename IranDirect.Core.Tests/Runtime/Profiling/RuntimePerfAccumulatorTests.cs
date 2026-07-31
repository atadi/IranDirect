namespace IranDirect.Core.Tests.Runtime.Profiling;

using IranDirect.Core.Runtime.Profiling;

public sealed class RuntimePerfAccumulatorTests
{
    [Fact]
    public void Snapshot_WhenEmpty_ReturnsEmpty()
    {
        RuntimePerfAccumulator accumulator = new();

        Assert.Empty(accumulator.Snapshot());
    }

    [Fact]
    public void Add_SingleSample_ProducesExactStats()
    {
        RuntimePerfAccumulator accumulator = new();

        accumulator.Add(
            RuntimePerfCategory.ExecutionRouteCreate,
            TimeSpan.FromMilliseconds(42));

        RuntimePerfCategorySummary summary =
            Assert.Single(accumulator.Snapshot());

        Assert.Equal(
            RuntimePerfCategory.ExecutionRouteCreate,
            summary.Category);
        Assert.Equal(1, summary.Count);
        Assert.Equal(42, summary.TotalMs);
        Assert.Equal(42, summary.AverageMs);
        Assert.Equal(42, summary.MinMs);
        Assert.Equal(42, summary.MaxMs);
        Assert.Equal(42, summary.P95Ms);
    }

    [Fact]
    public void Add_MultipleSamples_Aggregates()
    {
        RuntimePerfAccumulator accumulator = new();

        accumulator.Add(
            RuntimePerfCategory.ExecutionRouteVerify,
            TimeSpan.FromMilliseconds(10));
        accumulator.Add(
            RuntimePerfCategory.ExecutionRouteVerify,
            TimeSpan.FromMilliseconds(20));
        accumulator.Add(
            RuntimePerfCategory.ExecutionRouteVerify,
            TimeSpan.FromMilliseconds(30));

        RuntimePerfCategorySummary summary =
            Assert.Single(accumulator.Snapshot());

        Assert.Equal(3, summary.Count);
        Assert.Equal(60, summary.TotalMs);
        Assert.Equal(20, summary.AverageMs);
        Assert.Equal(10, summary.MinMs);
        Assert.Equal(30, summary.MaxMs);
    }

    [Fact]
    public void Add_ComputesP95()
    {
        RuntimePerfAccumulator accumulator = new();

        for (int i = 1; i <= 100; i++)
        {
            accumulator.Add(
                RuntimePerfCategory.ExecutionInventoryMutation,
                TimeSpan.FromMilliseconds(i));
        }

        RuntimePerfCategorySummary summary =
            Assert.Single(accumulator.Snapshot());

        Assert.Equal(100, summary.Count);
        Assert.Equal(95, summary.P95Ms);
        Assert.Equal(1, summary.MinMs);
        Assert.Equal(100, summary.MaxMs);
    }

    [Fact]
    public void Add_MultipleCategories_AreIndependent()
    {
        RuntimePerfAccumulator accumulator = new();

        accumulator.Add(
            RuntimePerfCategory.ObservationPrefixLoad,
            TimeSpan.FromMilliseconds(5));
        accumulator.Add(
            RuntimePerfCategory.PersistenceStateSave,
            TimeSpan.FromMilliseconds(10));
        accumulator.Add(
            RuntimePerfCategory.PersistenceStateSave,
            TimeSpan.FromMilliseconds(30));

        IReadOnlyList<RuntimePerfCategorySummary> snapshot =
            accumulator.Snapshot();

        Assert.Equal(2, snapshot.Count);

        RuntimePerfCategorySummary observation =
            Assert.Single(snapshot, s =>
                s.Category ==
                RuntimePerfCategory.ObservationPrefixLoad);
        Assert.Equal(1, observation.Count);
        Assert.Equal(5, observation.TotalMs);

        RuntimePerfCategorySummary persistence =
            Assert.Single(snapshot, s =>
                s.Category ==
                RuntimePerfCategory.PersistenceStateSave);
        Assert.Equal(2, persistence.Count);
        Assert.Equal(40, persistence.TotalMs);
        Assert.Equal(20, persistence.AverageMs);
    }

    [Fact]
    public void Add_Concurrent_PreservesAllSamples()
    {
        RuntimePerfAccumulator accumulator = new();
        const int taskCount = 8;
        const int samplesPerTask = 100;

        Parallel.For(0, taskCount, _ =>
        {
            for (int i = 0; i < samplesPerTask; i++)
            {
                accumulator.Add(
                    RuntimePerfCategory.ExecutionRouteCreate,
                    TimeSpan.FromMilliseconds(1));
            }
        });

        RuntimePerfCategorySummary summary =
            Assert.Single(accumulator.Snapshot());

        Assert.Equal(taskCount * samplesPerTask, summary.Count);
        Assert.Equal(
            taskCount * samplesPerTask, summary.TotalMs);
    }
}
