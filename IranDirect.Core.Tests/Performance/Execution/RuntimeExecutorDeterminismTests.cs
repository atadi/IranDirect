using IranDirect.Core.Runtime.Execution;
using IranDirect.Testing.Performance.Execution;
using IranDirect.Testing.Performance.Workloads;

namespace IranDirect.Core.Tests.Performance.Execution;

public sealed class RuntimeExecutorDeterminismTests
{
    private readonly RuntimeExecutionWorkloadGenerator _generator = new();

    [Fact]
    public async Task PrefixGroup_BoundedConcurrency_ReachesMaxDoP()
    {
        RuntimeExecutionWorkload workload = _generator.Generate(
            RuntimeExecutionWorkloadKind.PrefixRouteCreates,
            RuntimeWorkloadSize.Scale1K);
        RuntimeExecutionConcurrencyProbe probe = new();
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        ControlledRuntimeExecutionHandler handler = new(
            probe,
            (_, ct) => Task.WhenAny(release.Task, Task.Delay(Timeout.Infinite, ct)));
        RuntimeExecutor executor = new(handler);
        RuntimeExecutionPlan plan = new() { Steps = workload.Steps };

        Task<RuntimeExecutionResult> running = executor.ExecuteAsync(plan);

        await probe.WaitForActiveAsync(8);

        Assert.Equal(8, probe.MaxConcurrency);

        release.TrySetResult();
        RuntimeExecutionResult result = await running;

        Assert.Equal(RuntimeExecutionResultStatus.Completed, result.Status);
        Assert.Equal(1_000, handler.MutationCallCount);
        Assert.Equal(1, handler.VerifyCallCount);
        Assert.Equal(0, probe.Active);
    }

    [Fact]
    public async Task EndpointGroup_ExecutesSequentially()
    {
        RuntimeExecutionWorkload workload = _generator.Generate(
            RuntimeExecutionWorkloadKind.EndpointRouteCreates,
            RuntimeWorkloadSize.Scale1K);
        RuntimeExecutionConcurrencyProbe probe = new();
        ControlledRuntimeExecutionHandler handler = new(probe);
        RuntimeExecutor executor = new(handler);
        RuntimeExecutionPlan plan = new() { Steps = workload.Steps };

        RuntimeExecutionResult result = await executor.ExecuteAsync(plan);

        Assert.Equal(RuntimeExecutionResultStatus.Completed, result.Status);
        Assert.Equal(1, probe.MaxConcurrency);

        IReadOnlyList<RuntimeExecutionEvent> events = probe.Events;
        Assert.Equal(2_000, events.Count);
        for (int i = 0; i < events.Count; i += 2)
        {
            Assert.True(events[i].IsStart, $"Expected a start event at index {i}.");
            Assert.False(events[i + 1].IsStart, $"Expected an end event at index {i + 1}.");
            Assert.Equal(events[i].Identity, events[i + 1].Identity);
        }
    }

    [Fact]
    public async Task PrefixGroup_MixedLatency_WaitsForWholeGroupBeforeVerification()
    {
        RuntimeExecutionWorkload workload = _generator.Generate(
            RuntimeExecutionWorkloadKind.PrefixRouteCreates,
            RuntimeWorkloadSize.Scale5K);

        const int slowCount = 6;
        HashSet<string> slowIdentities = new(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < workload.Steps.Count; i += 700)
        {
            slowIdentities.Add(workload.Steps[i].Identity);
            if (slowIdentities.Count == slowCount)
            {
                break;
            }
        }

        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        RuntimeExecutionConcurrencyProbe probe = new();
        ControlledRuntimeExecutionHandler handler = new(
            probe,
            (step, ct) =>
                slowIdentities.Contains(step.Identity)
                    ? Task.WhenAny(release.Task, Task.Delay(Timeout.Infinite, ct))
                    : Task.CompletedTask);
        RuntimeExecutor executor = new(handler);
        RuntimeExecutionPlan plan = new() { Steps = workload.Steps };

        Task<RuntimeExecutionResult> running = executor.ExecuteAsync(plan);

        int fastCount = workload.Count - slowIdentities.Count;
        await WaitUntilAsync(
            () => probe.Active == slowIdentities.Count &&
                  handler.MutationCallCount == fastCount);

        Assert.Equal(0, handler.VerifyCallCount);

        release.TrySetResult();
        RuntimeExecutionResult result = await running;

        Assert.Equal(RuntimeExecutionResultStatus.Completed, result.Status);
        Assert.Equal(1, handler.VerifyCallCount);
        Assert.Equal(5_000, handler.MutationCallCount);
        Assert.Equal(0, probe.Active);
    }

    [Fact]
    public async Task CancellationDuringPrefixGroup_IsDeterministicAndNonHanging()
    {
        RuntimeExecutionWorkload workload = _generator.Generate(
            RuntimeExecutionWorkloadKind.PrefixRouteCreates,
            RuntimeWorkloadSize.Scale5K);
        RuntimeExecutionConcurrencyProbe probe = new();
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        ControlledRuntimeExecutionHandler handler = new(
            probe,
            (_, ct) => Task.WhenAny(release.Task, Task.Delay(Timeout.Infinite, ct)));
        RuntimeExecutor executor = new(handler);
        RuntimeExecutionPlan plan = new() { Steps = workload.Steps };
        using CancellationTokenSource cts = new();

        Task<RuntimeExecutionResult> running = executor.ExecuteAsync(
            plan,
            cancellationToken: cts.Token);

        await probe.WaitForActiveAsync(8);
        Assert.Equal(8, probe.MaxConcurrency);

        cts.Cancel();
        release.TrySetResult();
        RuntimeExecutionResult result = await running;

        Assert.Equal(RuntimeExecutionResultStatus.Cancelled, result.Status);
        Assert.DoesNotContain(
            result.StepResults,
            sr => sr.Status == RuntimeExecutionStepStatus.Succeeded);
        Assert.Contains(
            result.StepResults,
            sr => sr.Status == RuntimeExecutionStepStatus.Cancelled);
        Assert.Contains(
            result.StepResults,
            sr => sr.Status == RuntimeExecutionStepStatus.Skipped);
        Assert.Equal(
            5_000,
            result.StepResults.Count(
                sr => sr.Status is RuntimeExecutionStepStatus.Cancelled
                    or RuntimeExecutionStepStatus.Skipped));
        Assert.Equal(0, handler.MutationCallCount);
        Assert.Equal(0, handler.StepCallCount);
        Assert.Equal(0, handler.VerifyCallCount);
        Assert.Equal(0, probe.Active);
    }

    [Fact]
    public async Task Executor_ReusableAfterInFlightCancellation()
    {
        RuntimeExecutionWorkload workload = _generator.Generate(
            RuntimeExecutionWorkloadKind.PrefixRouteCreates,
            RuntimeWorkloadSize.Scale1K);
        RuntimeExecutionConcurrencyProbe probe = new();
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        ControlledRuntimeExecutionHandler handler = new(
            probe,
            (_, ct) => Task.WhenAny(release.Task, Task.Delay(Timeout.Infinite, ct)));
        RuntimeExecutor executor = new(handler);
        RuntimeExecutionPlan plan = new() { Steps = workload.Steps };
        using CancellationTokenSource cts = new();

        Task<RuntimeExecutionResult> firstRun = executor.ExecuteAsync(
            plan,
            cancellationToken: cts.Token);

        await probe.WaitForActiveAsync(8);
        cts.Cancel();
        release.TrySetResult();

        RuntimeExecutionResult cancelled = await firstRun;
        Assert.Equal(RuntimeExecutionResultStatus.Cancelled, cancelled.Status);
        Assert.Equal(0, probe.Active);

        RuntimeExecutionResult second = await executor.ExecuteAsync(plan);

        Assert.Equal(RuntimeExecutionResultStatus.Completed, second.Status);
        RuntimeExecutionResultVerifier.VerifyPlanOrder(plan, second);
        Assert.Equal(0, probe.Active);
    }

    [Fact]
    public async Task ConcurrentExecutors_AreIsolated()
    {
        RuntimeExecutionWorkload workloadA = _generator.Generate(
            RuntimeExecutionWorkloadKind.PrefixRouteCreates,
            RuntimeWorkloadSize.Scale5K);
        RuntimeExecutionWorkload workloadB = _generator.Generate(
            RuntimeExecutionWorkloadKind.EndpointRouteCreates,
            RuntimeWorkloadSize.Scale5K);
        RuntimeExecutionConcurrencyProbe probeA = new();
        RuntimeExecutionConcurrencyProbe probeB = new();
        ControlledRuntimeExecutionHandler handlerA = new(probeA);
        ControlledRuntimeExecutionHandler handlerB = new(probeB);
        RuntimeExecutor executorA = new(handlerA);
        RuntimeExecutor executorB = new(handlerB);
        RuntimeExecutionPlan planA = new() { Steps = workloadA.Steps };
        RuntimeExecutionPlan planB = new() { Steps = workloadB.Steps };

        Task<RuntimeExecutionResult> runA = executorA.ExecuteAsync(planA);
        Task<RuntimeExecutionResult> runB = executorB.ExecuteAsync(planB);
        RuntimeExecutionResult[] results = await Task.WhenAll(runA, runB);

        Assert.All(
            results,
            r => Assert.Equal(RuntimeExecutionResultStatus.Completed, r.Status));
        RuntimeExecutionResultVerifier.VerifyPlanOrder(planA, results[0]);
        RuntimeExecutionResultVerifier.VerifyPlanOrder(planB, results[1]);
        Assert.True(
            probeA.MaxConcurrency <= 8,
            $"Executor A exceeded the parallelism cap: {probeA.MaxConcurrency}.");
        Assert.True(
            probeB.MaxConcurrency <= 8,
            $"Executor B exceeded the parallelism cap: {probeB.MaxConcurrency}.");
        Assert.Equal(5_000, handlerA.MutationCallCount);
        Assert.Equal(5_000, handlerB.StepCallCount);
        Assert.Equal(0, probeA.Active);
        Assert.Equal(0, probeB.Active);
    }

    private static async Task WaitUntilAsync(
        Func<bool> condition,
        int timeoutMilliseconds = 30_000)
    {
        long deadline = Environment.TickCount64 + timeoutMilliseconds;
        while (!condition())
        {
            if (Environment.TickCount64 > deadline)
            {
                throw new TimeoutException(
                    "Condition was not satisfied within the timeout.");
            }

            await Task.Delay(25);
        }
    }
}
