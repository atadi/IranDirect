using PathVeer.Core.Runtime.Execution;
using PathVeer.Testing.Performance.Execution;
using PathVeer.Testing.Performance.Workloads;

namespace PathVeer.Core.Tests.Performance.Execution;

[Trait("Category", "Stress")]
public sealed class RuntimeExecutorStressTests
{
    private readonly RuntimeExecutionWorkloadGenerator _generator = new();

    [Fact]
    public async Task ExecuteAsync_25KPrefixCreates_AllSuccess()
    {
        RuntimeExecutionWorkload workload = _generator.Generate(
            RuntimeExecutionWorkloadKind.PrefixRouteCreates,
            RuntimeWorkloadSize.Scale25K);
        ControlledRuntimeExecutionHandler handler = new();
        RuntimeExecutor executor = new(handler);
        RuntimeExecutionPlan plan = new() { Steps = workload.Steps };

        RuntimeExecutionResult result = await executor.ExecuteAsync(plan);

        Assert.Equal(RuntimeExecutionResultStatus.Completed, result.Status);
        RuntimeExecutionResultVerifier.VerifyPlanOrder(plan, result);
        Assert.All(
            result.StepResults,
            sr => Assert.Equal(RuntimeExecutionStepStatus.Succeeded, sr.Status));
        Assert.Equal(25_000, handler.MutationCallCount);
        Assert.Equal(1, handler.VerifyCallCount);
    }

    [Fact]
    public async Task ExecuteAsync_50KMixedGroups_AllSuccess()
    {
        RuntimeExecutionWorkload workload = _generator.Generate(
            RuntimeExecutionWorkloadKind.MixedExecutionGroups,
            RuntimeWorkloadSize.Scale50K);
        ControlledRuntimeExecutionHandler handler = new();
        RuntimeExecutor executor = new(handler);
        RuntimeExecutionPlan plan = new() { Steps = workload.Steps };

        RuntimeExecutionResult result = await executor.ExecuteAsync(plan);

        Assert.Equal(RuntimeExecutionResultStatus.Completed, result.Status);
        RuntimeExecutionResultVerifier.VerifyPlanOrder(plan, result);
        Assert.All(
            result.StepResults,
            sr => Assert.Equal(RuntimeExecutionStepStatus.Succeeded, sr.Status));
    }

    [Fact]
    public async Task ExecuteAsync_50KPrefixCreates_ProgressStableAndConsistent()
    {
        RuntimeExecutionWorkload workload = _generator.Generate(
            RuntimeExecutionWorkloadKind.PrefixRouteCreates,
            RuntimeWorkloadSize.Scale50K);
        RecordingProgress progress = new();
        ControlledRuntimeExecutionHandler handler = new();
        RuntimeExecutor executor = new(handler);
        RuntimeExecutionPlan plan = new() { Steps = workload.Steps };

        RuntimeExecutionResult result = await executor.ExecuteAsync(plan, progress);

        Assert.Equal(RuntimeExecutionResultStatus.Completed, result.Status);
        RuntimeExecutionResultVerifier.VerifyFinalProgressStable(result, progress.Snapshots);
    }

    [Fact]
    public async Task ExecuteAsync_25KPrefixFailure_AllMutationsRunThenVerificationFails()
    {
        RuntimeExecutionWorkload workload = _generator.Generate(
            RuntimeExecutionWorkloadKind.PrefixRouteCreates,
            RuntimeWorkloadSize.Scale25K);
        string failIdentity = workload.Steps[12_500].Identity;
        ControlledRuntimeExecutionHandler handler = new()
        {
            FailIdentities = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                failIdentity
            }
        };
        RuntimeExecutor executor = new(handler);
        RuntimeExecutionPlan plan = new() { Steps = workload.Steps };

        RuntimeExecutionResult result = await executor.ExecuteAsync(plan);

        Assert.Equal(RuntimeExecutionResultStatus.PartiallyCompleted, result.Status);
        Assert.Equal(25_000, handler.MutationCallCount);
        Assert.Equal(1, handler.VerifyCallCount);
        Assert.Equal(
            24_999,
            result.StepResults.Count(sr => sr.Status == RuntimeExecutionStepStatus.Succeeded));
        Assert.Equal(
            1,
            result.StepResults.Count(sr => sr.Status == RuntimeExecutionStepStatus.Failed));
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
