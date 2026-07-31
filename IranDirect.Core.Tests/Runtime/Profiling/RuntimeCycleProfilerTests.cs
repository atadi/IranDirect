namespace IranDirect.Core.Tests.Runtime.Profiling;

using IranDirect.Core.Runtime.Profiling;

public sealed class RuntimeCycleProfilerTests
{
    [Fact]
    public void Measure_WithoutActiveCycle_DoesNotThrow()
    {
        RuntimeCycleProfiler profiler = new(enabled: true);

        using IDisposable scope = profiler.Measure(
            RuntimePerfCategory.ExecutionRouteCreate);

        Assert.NotNull(scope);
    }

    [Fact]
    public void BeginCycle_WhenDisabled_WritesNoReport()
    {
        using TempDirectory temp = new();
        Directory.CreateDirectory(temp.Path);
        RuntimeCycleProfiler profiler = new(
            enabled: false,
            new RuntimePerfReportStore(temp.Path));

        using (profiler.BeginCycle("enable"))
        {
            using IDisposable scope = profiler.Measure(
                RuntimePerfCategory.ExecutionRouteCreate);
        }

        Assert.Empty(Directory.GetFiles(temp.Path));
    }

    [Fact]
    public void BeginCycle_WhenEnabled_WritesSingleReport()
    {
        using TempDirectory temp = new();
        RuntimeCycleProfiler profiler = new(
            enabled: true,
            new RuntimePerfReportStore(temp.Path));

        using (profiler.BeginCycle("enable"))
        {
            using IDisposable scope = profiler.Measure(
                RuntimePerfCategory.ObservationPrefixLoad);
        }

        string path = Assert.Single(
            Directory.GetFiles(
                temp.Path, RuntimePerfReportStore.FilePattern));

        Assert.StartsWith("enable-", Path.GetFileName(path));

        RuntimeCyclePerfReport? report =
            new RuntimePerfReportStore(temp.Path).ReadLatest();

        Assert.NotNull(report);
        Assert.Equal("enable", report.Trigger);
        Assert.True(report.CompletedAt >= report.StartedAt);
    }

    [Fact]
    public void Measure_WithinCycle_AppearsInReportCategories()
    {
        using TempDirectory temp = new();
        RuntimeCycleProfiler profiler = new(
            enabled: true,
            new RuntimePerfReportStore(temp.Path));

        using (profiler.BeginCycle("repair"))
        {
            using (profiler.Measure(
                RuntimePerfCategory.ExecutionRouteVerify))
            {
            }

            using (profiler.Measure(
                RuntimePerfCategory.PersistenceStateSave))
            {
            }
        }

        RuntimeCyclePerfReport? report =
            new RuntimePerfReportStore(temp.Path).ReadLatest();

        Assert.NotNull(report);
        Assert.Equal("repair", report.Trigger);

        RuntimePerfCategorySummary verify = Assert.Single(
            report.Categories,
            c => c.Category ==
                RuntimePerfCategory.ExecutionRouteVerify);
        Assert.Equal(1, verify.Count);

        Assert.Single(
            report.Categories,
            c => c.Category ==
                RuntimePerfCategory.PersistenceStateSave);
    }

    [Fact]
    public void Measure_AfterCycleDisposed_IsNotRecorded()
    {
        using TempDirectory temp = new();
        RuntimeCycleProfiler profiler = new(
            enabled: true,
            new RuntimePerfReportStore(temp.Path));

        using (profiler.BeginCycle("enable"))
        {
        }

        using IDisposable scope = profiler.Measure(
            RuntimePerfCategory.ExecutionRouteDelete);

        RuntimeCyclePerfReport? report =
            new RuntimePerfReportStore(temp.Path).ReadLatest();

        Assert.NotNull(report);
        Assert.DoesNotContain(
            report.Categories,
            c => c.Category ==
                RuntimePerfCategory.ExecutionRouteDelete);
    }

    [Fact]
    public void CycleScope_DisposedTwice_WritesOnce()
    {
        using TempDirectory temp = new();
        RuntimeCycleProfiler profiler = new(
            enabled: true,
            new RuntimePerfReportStore(temp.Path));

        IDisposable cycle = profiler.BeginCycle("disable");
        cycle.Dispose();
        cycle.Dispose();

        Assert.Single(
            Directory.GetFiles(
                temp.Path, RuntimePerfReportStore.FilePattern));
    }

    [Fact]
    public void Noop_BeginCycle_WritesNothing()
    {
        using (RuntimeCycleProfiler.Noop.BeginCycle("enable"))
        {
            using IDisposable scope =
                RuntimeCycleProfiler.Noop.Measure(
                    RuntimePerfCategory.ExecutionRouteCreate);
        }

        Assert.False(RuntimeCycleProfiler.Noop.Enabled);
    }

    [Fact]
    public void BeginCycleIfNone_InsideActiveCycle_ReturnsNoopScope()
    {
        using TempDirectory temp = new();
        RuntimeCycleProfiler profiler = new(
            enabled: true,
            new RuntimePerfReportStore(temp.Path));

        using IDisposable outer = profiler.BeginCycleIfNone("enable");
        IDisposable nested = profiler.BeginCycleIfNone("disable");

        Assert.True(profiler.HasActiveCycle);
        nested.Dispose();
        outer.Dispose();

        RuntimeCyclePerfReport? report =
            new RuntimePerfReportStore(temp.Path).ReadLatest();

        Assert.NotNull(report);
        Assert.Equal("enable", report.Trigger);
        Assert.Single(
            Directory.GetFiles(
                temp.Path, RuntimePerfReportStore.FilePattern));
    }

    [Fact]
    public void BeginCycle_InsideActiveCycle_DoesNotReplaceTrigger()
    {
        using TempDirectory temp = new();
        RuntimeCycleProfiler profiler = new(
            enabled: true,
            new RuntimePerfReportStore(temp.Path));

        using (profiler.BeginCycle("enable"))
        {
            using (profiler.BeginCycle("repair"))
            {
                using IDisposable scope = profiler.Measure(
                    RuntimePerfCategory.ExecutionRouteCreate);
            }
        }

        RuntimeCyclePerfReport? report =
            new RuntimePerfReportStore(temp.Path).ReadLatest();

        Assert.NotNull(report);
        Assert.Equal("enable", report.Trigger);
        Assert.Single(
            Directory.GetFiles(
                temp.Path, RuntimePerfReportStore.FilePattern));
    }

    [Fact]
    public void NestedScope_Dispose_DoesNotClearOuterContext()
    {
        using TempDirectory temp = new();
        RuntimeCycleProfiler profiler = new(
            enabled: true,
            new RuntimePerfReportStore(temp.Path));

        using (profiler.BeginCycle("enable"))
        {
            IDisposable nested = profiler.BeginCycleIfNone("repair");
            Assert.True(profiler.HasActiveCycle);

            nested.Dispose();

            Assert.True(profiler.HasActiveCycle);

            using IDisposable scope = profiler.Measure(
                RuntimePerfCategory.ExecutionRouteVerify);
        }

        RuntimeCyclePerfReport? report =
            new RuntimePerfReportStore(temp.Path).ReadLatest();

        Assert.NotNull(report);
        Assert.Equal("enable", report.Trigger);
        Assert.Single(
            report.Categories,
            c => c.Category ==
                RuntimePerfCategory.ExecutionRouteVerify);
    }

    [Fact]
    public void FailedCycle_StillWritesReport()
    {
        using TempDirectory temp = new();
        RuntimePerfReportStore store = new(temp.Path);
        RuntimeCycleProfiler profiler = new(
            enabled: true,
            store);

        using (profiler.BeginCycle("enable"))
        {
            using IDisposable scope = profiler.Measure(
                RuntimePerfCategory.ExecutionRouteCreate);

            profiler.SetCycleOutcome(
                CycleCompletionStatus.Failed,
                "Inventory persistence failed.",
                plannedSteps: 10,
                completedSteps: 6);
        }

        RuntimeCyclePerfReport? report = store.ReadLatest();

        Assert.NotNull(report);
        Assert.Equal("enable", report.Trigger);
        Assert.Equal(
            CycleCompletionStatus.Failed,
            report.CompletionStatus);
        Assert.Contains(
            "Inventory persistence failed.",
            report.ErrorSummary);
        Assert.Equal(10, report.PlannedSteps);
        Assert.Equal(6, report.CompletedSteps);
    }

    [Fact]
    public void PartiallyCompletedCycle_WritesReport()
    {
        using TempDirectory temp = new();
        RuntimePerfReportStore store = new(temp.Path);
        RuntimeCycleProfiler profiler = new(
            enabled: true,
            store);

        using (profiler.BeginCycle("enable"))
        {
            profiler.SetCycleOutcome(
                CycleCompletionStatus.PartiallyCompleted,
                "Some routes failed.");
        }

        RuntimeCyclePerfReport? report = store.ReadLatest();

        Assert.NotNull(report);
        Assert.Equal(
            CycleCompletionStatus.PartiallyCompleted,
            report.CompletionStatus);
    }

    [Fact]
    public void CancelledCycle_WritesCancelledReport()
    {
        using TempDirectory temp = new();
        RuntimePerfReportStore store = new(temp.Path);
        RuntimeCycleProfiler profiler = new(
            enabled: true,
            store);

        using (profiler.BeginCycle("disable"))
        {
            profiler.SetCycleOutcome(
                CycleCompletionStatus.Cancelled,
                "Operation was cancelled.");
        }

        RuntimeCyclePerfReport? report = store.ReadLatest();

        Assert.NotNull(report);
        Assert.Equal("disable", report.Trigger);
        Assert.Equal(
            CycleCompletionStatus.Cancelled,
            report.CompletionStatus);
    }

    [Fact]
    public async Task ParallelChildTasks_RecordIntoActiveOuterContext()
    {
        using TempDirectory temp = new();
        RuntimePerfReportStore store = new(temp.Path);
        RuntimeCycleProfiler profiler = new(
            enabled: true,
            store);

        using (profiler.BeginCycle("enable"))
        {
            Task[] tasks = Enumerable.Range(0, 8)
                .Select(_ => Task.Run(() =>
                {
                    using IDisposable scope = profiler.Measure(
                        RuntimePerfCategory.ExecutionRouteCreate);
                    Thread.SpinWait(1000);
                }))
                .ToArray();

            await Task.WhenAll(tasks);
        }

        RuntimeCyclePerfReport? report = store.ReadLatest();

        Assert.NotNull(report);
        RuntimePerfCategorySummary create = Assert.Single(
            report.Categories,
            c => c.Category ==
                RuntimePerfCategory.ExecutionRouteCreate);
        Assert.Equal(8, create.Count);
    }

    [Fact]
    public async Task AsyncFlow_KeepsContextAcrossAwaits()
    {
        using TempDirectory temp = new();
        RuntimePerfReportStore store = new(temp.Path);
        RuntimeCycleProfiler profiler = new(
            enabled: true,
            store);

        using (profiler.BeginCycle("enable"))
        {
            using IDisposable scope = profiler.Measure(
                RuntimePerfCategory.ExecutionRouteCreate);
            await Task.Delay(5);
        }

        RuntimeCyclePerfReport? report = store.ReadLatest();

        Assert.NotNull(report);
        Assert.Single(
            report.Categories,
            c => c.Category ==
                RuntimePerfCategory.ExecutionRouteCreate);
    }

    [Fact]
    public void ReportWriteFailure_DoesNotAlterRuntimeResult()
    {
        using TempDirectory temp = new();
        Directory.CreateDirectory(temp.Path);
        string filePath = Path.Combine(temp.Path, "a-file");
        File.WriteAllText(filePath, "not a directory");

        RuntimeCycleProfiler profiler = new(
            enabled: true,
            new RuntimePerfReportStore(filePath));

        bool completed = false;

        using (profiler.BeginCycle("enable"))
        {
            using IDisposable scope = profiler.Measure(
                RuntimePerfCategory.ExecutionRouteCreate);
            completed = true;
        }

        Assert.True(completed);
        Assert.False(profiler.HasActiveCycle);
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } =
            System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"IranPerfProfilerTest_{Guid.NewGuid()}");

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                    Directory.Delete(Path, recursive: true);
            }
            catch
            {
                // Best-effort cleanup.
            }
        }
    }
}
