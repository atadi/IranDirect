using IranDirect.Core.Runtime.Execution;
using IranDirect.Testing.Performance.Execution;
using IranDirect.Testing.Performance.Workloads;

namespace IranDirect.Core.Tests.Performance.Execution;

public sealed class RuntimeExecutorScaleTests
{
    private readonly RuntimeExecutionWorkloadGenerator _generator = new();

    public static IEnumerable<object[]> AllSuccessCases() =>
        from kind in Enum.GetValues<RuntimeExecutionWorkloadKind>()
        from size in new[]
        {
            RuntimeWorkloadSize.Scale1K,
            RuntimeWorkloadSize.Scale2K,
            RuntimeWorkloadSize.Scale5K,
            RuntimeWorkloadSize.Scale10K
        }
        select new object[] { kind, size };

    [Theory]
    [MemberData(nameof(AllSuccessCases))]
    public async Task ExecuteAsync_AllSuccess_ReturnsCompletedInPlanOrder(
        RuntimeExecutionWorkloadKind kind,
        RuntimeWorkloadSize size)
    {
        RuntimeExecutionWorkload workload = _generator.Generate(kind, size);
        RuntimeExecutionPlan plan = new() { Steps = workload.Steps };
        ControlledRuntimeExecutionHandler handler = new();
        RuntimeExecutor executor = new(handler);

        RuntimeExecutionResult result = await executor.ExecuteAsync(plan);

        Assert.Equal(RuntimeExecutionResultStatus.Completed, result.Status);
        RuntimeExecutionResultVerifier.VerifyPlanOrder(plan, result);
        Assert.All(
            result.StepResults,
            sr => Assert.Equal(RuntimeExecutionStepStatus.Succeeded, sr.Status));
        Assert.True(result.MutatedInfrastructure);
    }

    [Fact]
    public async Task ExecuteAsync_AllSuccess_ProgressFinalSnapshotStableAndConsistentAtScale()
    {
        RuntimeExecutionWorkload workload = _generator.Generate(
            RuntimeExecutionWorkloadKind.PrefixRouteCreates,
            RuntimeWorkloadSize.Scale10K);
        RecordingProgress progress = new();
        ControlledRuntimeExecutionHandler handler = new();
        RuntimeExecutor executor = new(handler);
        RuntimeExecutionPlan plan = new() { Steps = workload.Steps };

        RuntimeExecutionResult result = await executor.ExecuteAsync(plan, progress);

        Assert.Equal(RuntimeExecutionResultStatus.Completed, result.Status);
        RuntimeExecutionResultVerifier.VerifyFinalProgressStable(result, progress.Snapshots);
    }

    [Fact]
    public async Task ExecuteAsync_AllSuccess_ProgressNoMoreThanTotalSteps()
    {
        RuntimeExecutionWorkload workload = _generator.Generate(
            RuntimeExecutionWorkloadKind.EndpointRouteCreates,
            RuntimeWorkloadSize.Scale5K);
        RecordingProgress progress = new();
        ControlledRuntimeExecutionHandler handler = new();
        RuntimeExecutor executor = new(handler);
        RuntimeExecutionPlan plan = new() { Steps = workload.Steps };

        await executor.ExecuteAsync(plan, progress);

        Assert.All(
            progress.Snapshots,
            snapshot => Assert.True(
                snapshot.ProcessedSteps <= snapshot.TotalSteps,
                $"ProcessedSteps {snapshot.ProcessedSteps} exceeds TotalSteps {snapshot.TotalSteps}."));
    }

    [Fact]
    public async Task ExecuteAsync_SingleFailureEarly_ReturnsFailedAndSkipsRest()
    {
        RuntimeExecutionWorkload workload = _generator.Generate(
            RuntimeExecutionWorkloadKind.EndpointRouteCreates,
            RuntimeWorkloadSize.Scale1K);
        string failIdentity = workload.Steps[0].Identity;
        ControlledRuntimeExecutionHandler handler = new()
        {
            FailIdentities = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { failIdentity }
        };
        RuntimeExecutor executor = new(handler);
        RuntimeExecutionPlan plan = new() { Steps = workload.Steps };

        RuntimeExecutionResult result = await executor.ExecuteAsync(plan);

        Assert.Equal(RuntimeExecutionResultStatus.Failed, result.Status);
        Assert.Equal(RuntimeExecutionStepStatus.Failed, result.StepResults[0].Status);
        Assert.All(
            result.StepResults.Skip(1),
            sr => Assert.Equal(RuntimeExecutionStepStatus.Skipped, sr.Status));
        Assert.False(result.MutatedInfrastructure);
        Assert.Equal(1, handler.StepCallCount);
    }

    [Fact]
    public async Task ExecuteAsync_SingleFailureMiddle_ReturnsPartiallyCompleted()
    {
        RuntimeExecutionWorkload workload = _generator.Generate(
            RuntimeExecutionWorkloadKind.EndpointRouteCreates,
            RuntimeWorkloadSize.Scale2K);
        const int failIndex = 1_000;
        string failIdentity = workload.Steps[failIndex].Identity;
        ControlledRuntimeExecutionHandler handler = new()
        {
            FailIdentities = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { failIdentity }
        };
        RuntimeExecutor executor = new(handler);
        RuntimeExecutionPlan plan = new() { Steps = workload.Steps };

        RuntimeExecutionResult result = await executor.ExecuteAsync(plan);

        Assert.Equal(RuntimeExecutionResultStatus.PartiallyCompleted, result.Status);
        Assert.All(
            result.StepResults.Take(failIndex),
            sr => Assert.Equal(RuntimeExecutionStepStatus.Succeeded, sr.Status));
        Assert.Equal(RuntimeExecutionStepStatus.Failed, result.StepResults[failIndex].Status);
        Assert.All(
            result.StepResults.Skip(failIndex + 1),
            sr => Assert.Equal(RuntimeExecutionStepStatus.Skipped, sr.Status));
        Assert.True(result.MutatedInfrastructure);
        Assert.Equal(failIndex + 1, handler.StepCallCount);
    }

    [Fact]
    public async Task ExecuteAsync_SingleFailureLate_NearlyCompletes()
    {
        RuntimeExecutionWorkload workload = _generator.Generate(
            RuntimeExecutionWorkloadKind.EndpointRouteDeletes,
            RuntimeWorkloadSize.Scale5K);
        const int failIndex = 4_999;
        string failIdentity = workload.Steps[failIndex].Identity;
        ControlledRuntimeExecutionHandler handler = new()
        {
            FailIdentities = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { failIdentity }
        };
        RuntimeExecutor executor = new(handler);
        RuntimeExecutionPlan plan = new() { Steps = workload.Steps };

        RuntimeExecutionResult result = await executor.ExecuteAsync(plan);

        Assert.Equal(RuntimeExecutionResultStatus.PartiallyCompleted, result.Status);
        Assert.Equal(5_000, handler.StepCallCount);
        Assert.Equal(
            4_999,
            result.StepResults.Count(sr => sr.Status == RuntimeExecutionStepStatus.Succeeded));
        Assert.Equal(RuntimeExecutionStepStatus.Failed, result.StepResults[failIndex].Status);
    }

    [Fact]
    public async Task ExecuteAsync_PrefixGroupFailure_AllMutationsRunThenVerificationFails()
    {
        RuntimeExecutionWorkload workload = _generator.Generate(
            RuntimeExecutionWorkloadKind.PrefixRouteCreates,
            RuntimeWorkloadSize.Scale10K);
        const int failIndex = 5_000;
        string failIdentity = workload.Steps[failIndex].Identity;
        ControlledRuntimeExecutionHandler handler = new()
        {
            FailIdentities = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { failIdentity }
        };
        RuntimeExecutor executor = new(handler);
        RuntimeExecutionPlan plan = new() { Steps = workload.Steps };

        RuntimeExecutionResult result = await executor.ExecuteAsync(plan);

        Assert.Equal(RuntimeExecutionResultStatus.PartiallyCompleted, result.Status);
        Assert.Equal(10_000, handler.MutationCallCount);
        Assert.Equal(1, handler.VerifyCallCount);
        Assert.Equal(RuntimeExecutionStepStatus.Failed, result.StepResults[failIndex].Status);
        Assert.Equal(
            9_999,
            result.StepResults.Count(sr => sr.Status == RuntimeExecutionStepStatus.Succeeded));
    }

    [Fact]
    public async Task ExecuteAsync_MultipleFailuresAcrossGroups_StopsSequentialGroupAndLaterGroups()
    {
        RuntimeExecutionWorkload workload = _generator.Generate(
            RuntimeExecutionWorkloadKind.MixedExecutionGroups,
            RuntimeWorkloadSize.Scale10K);
        int addEndpointCount = workload.AddEndpointRouteCount;
        int removePrefixCount = workload.RemovePrefixRouteCount;

        HashSet<string> failures = new(StringComparer.OrdinalIgnoreCase)
        {
            workload.Steps[100].Identity,
            workload.Steps[addEndpointCount + 500].Identity,
            workload.Steps[addEndpointCount + removePrefixCount + 100].Identity,
            workload.Steps[addEndpointCount + removePrefixCount + 900].Identity,
            workload.Steps[addEndpointCount + removePrefixCount + 1_700].Identity
        };

        ControlledRuntimeExecutionHandler handler = new() { FailIdentities = failures };
        RuntimeExecutor executor = new(handler);
        RuntimeExecutionPlan plan = new() { Steps = workload.Steps };

        RuntimeExecutionResult result = await executor.ExecuteAsync(plan);

        Assert.Equal(RuntimeExecutionResultStatus.PartiallyCompleted, result.Status);
        Assert.Equal(RuntimeExecutionStepStatus.Failed, result.StepResults[100].Status);
        Assert.All(
            result.StepResults.Skip(101).Take(addEndpointCount - 101),
            sr => Assert.Equal(RuntimeExecutionStepStatus.Skipped, sr.Status));
        Assert.All(
            result.StepResults.Skip(addEndpointCount),
            sr => Assert.Equal(RuntimeExecutionStepStatus.Skipped, sr.Status));
        Assert.True(result.MutatedInfrastructure);
    }

    [Fact]
    public async Task ExecuteAsync_BenignRaceMutations_CompensatedByGroupVerification()
    {
        RuntimeExecutionWorkload workload = _generator.Generate(
            RuntimeExecutionWorkloadKind.PrefixRouteCreates,
            RuntimeWorkloadSize.Scale5K);
        HashSet<string> benign = new(StringComparer.OrdinalIgnoreCase)
        {
            workload.Steps[100].Identity,
            workload.Steps[2_000].Identity,
            workload.Steps[4_999].Identity
        };
        ControlledRuntimeExecutionHandler handler = new() { BenignRaceIdentities = benign };
        RuntimeExecutor executor = new(handler);
        RuntimeExecutionPlan plan = new() { Steps = workload.Steps };

        RuntimeExecutionResult result = await executor.ExecuteAsync(plan);

        Assert.Equal(RuntimeExecutionResultStatus.Completed, result.Status);
        Assert.All(
            result.StepResults,
            sr => Assert.Equal(RuntimeExecutionStepStatus.Succeeded, sr.Status));
        Assert.Equal(1, handler.VerifyCallCount);
        Assert.Equal(5_000, handler.VerifiedIdentities.Count);
    }

    [Fact]
    public async Task ExecuteAsync_CancellationBeforeStart_ThrowsAndCallsNothing()
    {
        using CancellationTokenSource cts = new();
        cts.Cancel();

        RuntimeExecutionWorkload workload = _generator.Generate(
            RuntimeExecutionWorkloadKind.MixedExecutionGroups,
            RuntimeWorkloadSize.Scale1K);
        RuntimeExecutionConcurrencyProbe probe = new();
        ControlledRuntimeExecutionHandler handler = new(probe);
        RuntimeExecutor executor = new(handler);
        RuntimeExecutionPlan plan = new() { Steps = workload.Steps };

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => executor.ExecuteAsync(plan, cancellationToken: cts.Token));

        Assert.Empty(probe.Events);
        Assert.Equal(0, handler.StepCallCount);
        Assert.Equal(0, handler.MutationCallCount);
    }

    [Fact]
    public async Task ExecuteAsync_ExecutorReusableAfterFailure()
    {
        RuntimeExecutionWorkload workload = _generator.Generate(
            RuntimeExecutionWorkloadKind.PrefixRouteCreates,
            RuntimeWorkloadSize.Scale1K);
        ControlledRuntimeExecutionHandler handler = new()
        {
            FailIdentities = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                workload.Steps[500].Identity
            }
        };
        RuntimeExecutor executor = new(handler);
        RuntimeExecutionPlan plan = new() { Steps = workload.Steps };

        RuntimeExecutionResult first = await executor.ExecuteAsync(plan);

        Assert.Equal(RuntimeExecutionResultStatus.PartiallyCompleted, first.Status);

        handler.FailIdentities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        RuntimeExecutionResult second = await executor.ExecuteAsync(plan);

        Assert.Equal(RuntimeExecutionResultStatus.Completed, second.Status);
        RuntimeExecutionResultVerifier.VerifyPlanOrder(plan, second);
    }

    [Fact]
    public async Task ExecuteAsync_PerStepHandler_BoundedConcurrencyAtScale()
    {
        RuntimeExecutionWorkload workload = _generator.Generate(
            RuntimeExecutionWorkloadKind.PrefixRouteCreates,
            RuntimeWorkloadSize.Scale10K);
        RuntimeExecutionConcurrencyProbe probe = new();
        PerStepExecutionHandler handler = new(probe);
        RuntimeExecutor executor = new(handler);
        RuntimeExecutionPlan plan = new() { Steps = workload.Steps };

        RuntimeExecutionResult result = await executor.ExecuteAsync(plan);

        Assert.Equal(RuntimeExecutionResultStatus.Completed, result.Status);
        RuntimeExecutionResultVerifier.VerifyPlanOrder(plan, result);
        Assert.Equal(10_000, handler.StepCallCount);
        Assert.Equal(0, handler.VerifyCallCount);
        Assert.True(
            probe.MaxConcurrency <= 8,
            $"Expected max concurrency <= 8, got {probe.MaxConcurrency}.");
        Assert.Equal(0, probe.Active);
    }

    private sealed class RecordingProgress : IProgress<RuntimeExecutionProgress>
    {
        private readonly object _lock = new();
        private readonly List<RuntimeExecutionProgress> _values = [];

        public IReadOnlyList<RuntimeExecutionProgress> Snapshots
        {
            get
            {
                lock (_lock)
                {
                    return _values.ToArray();
                }
            }
        }

        public void Report(RuntimeExecutionProgress value)
        {
            lock (_lock)
            {
                _values.Add(value);
            }
        }
    }
}
