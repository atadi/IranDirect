namespace IranDirect.Core.Tests.Runtime.Execution;

using System.Diagnostics;
using IranDirect.Core.Runtime.Execution;

public sealed class RuntimeExecutorTests
{
    private static readonly RuntimeExecutionStep SampleEndpointStep = new()
    {
        Kind = RuntimeExecutionStepKind.AddEndpointRoute,
        Identity = "10.0.0.1/32|192.168.1.1|10",
        DestinationPrefix = "10.0.0.1/32",
        Gateway = "192.168.1.1",
        InterfaceIndex = 10,
        Metric = 1,
        Description = "Test endpoint route."
    };

    private static readonly RuntimeExecutionStep SamplePrefixStep = new()
    {
        Kind = RuntimeExecutionStepKind.AddPrefixRoute,
        Identity = "203.0.113.0/24|192.168.1.1|10",
        DestinationPrefix = "203.0.113.0/24",
        Gateway = "192.168.1.1",
        InterfaceIndex = 10,
        Metric = 256,
        Description = "Test prefix route."
    };

    [Fact]
    public async Task ExecuteAsync_NullPlan_Throws()
    {
        FakeStepHandler handler = new();
        RuntimeExecutor executor = new(handler);

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => executor.ExecuteAsync(null!));
    }

    [Fact]
    public async Task ExecuteAsync_EmptyPlan_ReturnsNoExecutionRequired()
    {
        FakeStepHandler handler = new();
        RuntimeExecutor executor = new(handler);
        RuntimeExecutionPlan plan = new() { Steps = [] };

        RuntimeExecutionResult result = await executor.ExecuteAsync(plan);

        Assert.Equal(
            RuntimeExecutionResultStatus.NoExecutionRequired,
            result.Status);
        Assert.Empty(result.StepResults);
        Assert.False(result.MutatedInfrastructure);
    }

    [Fact]
    public async Task ExecuteAsync_AllSucceed_ReturnsCompleted()
    {
        FakeStepHandler handler = new();
        RuntimeExecutor executor = new(handler);
        RuntimeExecutionPlan plan = CreatePrefixPlan(3);

        RuntimeExecutionResult result = await executor.ExecuteAsync(plan);

        Assert.Equal(
            RuntimeExecutionResultStatus.Completed,
            result.Status);
        Assert.Equal(3, result.StepResults.Count);
        Assert.All(result.StepResults,
            sr => Assert.Equal(
                RuntimeExecutionStepStatus.Succeeded, sr.Status));
        Assert.True(result.MutatedInfrastructure);
    }

    [Fact]
    public async Task ExecuteAsync_HandlerCalledOncePerStep()
    {
        FakeStepHandler handler = new();
        RuntimeExecutor executor = new(handler);
        RuntimeExecutionPlan plan = CreatePrefixPlan(2);

        await executor.ExecuteAsync(plan);

        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task ExecuteAsync_FirstStepFails_ReturnsFailedAndSkipsRemaining()
    {
        FakeStepHandler handler = new(failOnStep: 0);
        RuntimeExecutor executor = new(handler);
        RuntimeExecutionPlan plan = CreateEndpointPlan(3);

        RuntimeExecutionResult result = await executor.ExecuteAsync(plan);

        Assert.Equal(
            RuntimeExecutionResultStatus.Failed,
            result.Status);
        Assert.Equal(3, result.StepResults.Count);
        Assert.Equal(
            RuntimeExecutionStepStatus.Failed,
            result.StepResults[0].Status);
        Assert.Equal(
            RuntimeExecutionStepStatus.Skipped,
            result.StepResults[1].Status);
        Assert.Equal(
            RuntimeExecutionStepStatus.Skipped,
            result.StepResults[2].Status);
        Assert.False(result.MutatedInfrastructure);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task ExecuteAsync_SecondStepFails_ReturnsPartiallyCompleted()
    {
        FakeStepHandler handler = new(succeedAll: true, failOnStep: 1);
        RuntimeExecutor executor = new(handler);
        RuntimeExecutionPlan plan = CreateEndpointPlan(3);

        RuntimeExecutionResult result = await executor.ExecuteAsync(plan);

        Assert.Equal(
            RuntimeExecutionResultStatus.PartiallyCompleted,
            result.Status);
        Assert.Equal(3, result.StepResults.Count);
        Assert.Equal(
            RuntimeExecutionStepStatus.Succeeded,
            result.StepResults[0].Status);
        Assert.Equal(
            RuntimeExecutionStepStatus.Failed,
            result.StepResults[1].Status);
        Assert.Equal(
            RuntimeExecutionStepStatus.Skipped,
            result.StepResults[2].Status);
        Assert.True(result.MutatedInfrastructure);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task ExecuteAsync_CancellationBeforeFirstStep_Propagates()
    {
        using CancellationTokenSource cts = new();
        FakeStepHandler handler = new();
        RuntimeExecutor executor = new(handler);
        RuntimeExecutionPlan plan = CreatePrefixPlan(3);

        cts.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => executor.ExecuteAsync(plan, cancellationToken: cts.Token));
    }

    [Fact]
    public async Task ExecuteAsync_CancellationAfterSuccess_ReturnsPartiallyCompleted()
    {
        using CancellationTokenSource cts = new();
        FakeStepHandler handler = new(succeedAll: true, cancelOnStep: 1);
        RuntimeExecutor executor = new(handler);
        RuntimeExecutionPlan plan = CreateEndpointPlan(3);

        RuntimeExecutionResult result = await executor.ExecuteAsync(
            plan, cancellationToken: cts.Token);

        Assert.Equal(
            RuntimeExecutionResultStatus.PartiallyCompleted,
            result.Status);
        Assert.Equal(3, result.StepResults.Count);
        Assert.Equal(
            RuntimeExecutionStepStatus.Succeeded,
            result.StepResults[0].Status);
        Assert.Equal(
            RuntimeExecutionStepStatus.Cancelled,
            result.StepResults[1].Status);
        Assert.Equal(
            RuntimeExecutionStepStatus.Skipped,
            result.StepResults[2].Status);
        Assert.True(result.MutatedInfrastructure);
    }

    [Fact]
    public async Task ExecuteAsync_StepsResultsInPlanOrder()
    {
        FakeStepHandler handler = new(succeedAll: true);
        RuntimeExecutor executor = new(handler);
        RuntimeExecutionPlan plan = new()
        {
            Steps =
            [
                SampleEndpointStep with
                {
                    Kind = RuntimeExecutionStepKind.AddEndpointRoute,
                    Identity = "first|1.1.1.1|1"
                },
                SampleEndpointStep with
                {
                    Kind = RuntimeExecutionStepKind.RemoveEndpointRoute,
                    Identity = "second|2.2.2.2|2"
                }
            ]
        };

        RuntimeExecutionResult result = await executor.ExecuteAsync(plan);

        Assert.Equal(2, result.StepResults.Count);
        Assert.Equal("first|1.1.1.1|1", result.StepResults[0].StepIdentity);
        Assert.Equal("second|2.2.2.2|2", result.StepResults[1].StepIdentity);
    }

    [Fact]
    public async Task ExecuteAsync_ExactStepResultsPreserved()
    {
        RuntimeExecutionStepResult expected = new()
        {
            StepIdentity = SamplePrefixStep.Identity,
            Kind = RuntimeExecutionStepKind.AddPrefixRoute,
            DestinationPrefix = "test",
            Status = RuntimeExecutionStepStatus.Succeeded
        };
        FakeStepHandler handler = new(predefinedResult: expected);
        RuntimeExecutor executor = new(handler);
        RuntimeExecutionPlan plan = CreatePrefixPlan(1);

        RuntimeExecutionResult result = await executor.ExecuteAsync(plan);

        Assert.Same(expected, result.StepResults[0]);
    }

    [Fact]
    public async Task ExecuteAsync_MutatedInfrastructure_TrueAfterSuccess()
    {
        FakeStepHandler handler = new();
        RuntimeExecutor executor = new(handler);
        RuntimeExecutionPlan plan = CreatePrefixPlan(2);

        RuntimeExecutionResult result = await executor.ExecuteAsync(plan);

        Assert.True(result.MutatedInfrastructure);
    }

    [Fact]
    public async Task ExecuteAsync_MutatedInfrastructure_FalseWhenAllSkipped()
    {
        FakeStepHandler handler = new(succeedAll: false, failOnStep: 0);
        RuntimeExecutor executor = new(handler);
        RuntimeExecutionPlan plan = CreateEndpointPlan(2);

        RuntimeExecutionResult result = await executor.ExecuteAsync(plan);

        Assert.False(result.MutatedInfrastructure);
    }

    [Fact]
    public async Task ExecuteAsync_HandlerNotCalledAfterFailure()
    {
        FakeStepHandler handler = new(succeedAll: true, failOnStep: 1);
        RuntimeExecutor executor = new(handler);
        RuntimeExecutionPlan plan = CreateEndpointPlan(5);

        await executor.ExecuteAsync(plan);

        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task AddEndpointGroup_CompletesBeforePrefixGroups()
    {
        List<string> callOrder = [];
        object callLock = new();
        FakeStepHandler handler = new(trackOrder: callOrder, callLock: callLock);
        RuntimeExecutor executor = new(handler);

        IReadOnlyList<RuntimeExecutionStep> steps =
        [
            SampleEndpointStep with
            {
                Kind = RuntimeExecutionStepKind.AddEndpointRoute,
                Identity = "ep1|1.1.1.1|10"
            },
            SamplePrefixStep with
            {
                Kind = RuntimeExecutionStepKind.RemovePrefixRoute,
                Identity = "pf1|2.2.2.2|20"
            },
            SamplePrefixStep with
            {
                Kind = RuntimeExecutionStepKind.AddPrefixRoute,
                Identity = "pf2|3.3.3.3|30"
            }
        ];

        RuntimeExecutionPlan plan = new() { Steps = steps };
        await executor.ExecuteAsync(plan);

        int ep1Idx;
        int rmPfxIdx;
        int addPfxIdx;
        lock (callLock)
        {
            ep1Idx = callOrder.IndexOf("ep1|1.1.1.1|10");
            rmPfxIdx = callOrder.IndexOf("pf1|2.2.2.2|20");
            addPfxIdx = callOrder.IndexOf("pf2|3.3.3.3|30");
        }

        Assert.True(ep1Idx >= 0);
        Assert.True(rmPfxIdx >= 0);
        Assert.True(addPfxIdx >= 0);
        Assert.True(ep1Idx < rmPfxIdx, "AddEndpointRoute must execute before RemovePrefixRoute");
        Assert.True(rmPfxIdx < addPfxIdx, "RemovePrefixRoute must execute before AddPrefixRoute");
    }

    [Fact]
    public async Task RemoveEndpointGroup_StartsOnlyAfterAllPrefixGroups()
    {
        List<string> callOrder = [];
        object callLock = new();
        FakeStepHandler handler = new(trackOrder: callOrder, callLock: callLock);
        RuntimeExecutor executor = new(handler);

        IReadOnlyList<RuntimeExecutionStep> steps =
        [
            SamplePrefixStep with
            {
                Kind = RuntimeExecutionStepKind.AddPrefixRoute,
                Identity = "pf1|2.2.2.2|20"
            },
            SampleEndpointStep with
            {
                Kind = RuntimeExecutionStepKind.RemoveEndpointRoute,
                Identity = "ep2|4.4.4.4|40"
            }
        ];

        RuntimeExecutionPlan plan = new() { Steps = steps };
        await executor.ExecuteAsync(plan);

        int addPfxIdx;
        int rmEpIdx;
        lock (callLock)
        {
            addPfxIdx = callOrder.IndexOf("pf1|2.2.2.2|20");
            rmEpIdx = callOrder.IndexOf("ep2|4.4.4.4|40");
        }

        Assert.True(addPfxIdx >= 0);
        Assert.True(rmEpIdx >= 0);
        Assert.True(addPfxIdx < rmEpIdx, "AddPrefixRoute must complete before RemoveEndpointRoute");
    }

    [Fact]
    public async Task PrefixResults_PreservePlanOrder()
    {
        FakeStepHandler handler = new(succeedAll: true, delayMs: 5);
        RuntimeExecutor executor = new(handler);

        IReadOnlyList<RuntimeExecutionStep> steps = Enumerable.Range(0, 10)
            .Select(i => SamplePrefixStep with
            {
                Kind = RuntimeExecutionStepKind.AddPrefixRoute,
                Identity = $"{i}|1.1.1.{i}|{10 + i}",
                DestinationPrefix = $"203.0.113.{i}/24"
            })
            .ToArray();

        RuntimeExecutionPlan plan = new() { Steps = steps };
        RuntimeExecutionResult result = await executor.ExecuteAsync(plan);

        for (int i = 0; i < steps.Count; i++)
        {
            Assert.Equal(steps[i].Identity, result.StepResults[i].StepIdentity);
        }
    }

    [Fact]
    public async Task Failure_StopsSchedulingNewPrefixWork()
    {
        FakeStepHandler handler = new(succeedAll: true, failOnStep: 0);
        RuntimeExecutor executor = new(handler);

        IReadOnlyList<RuntimeExecutionStep> steps =
        [
            SampleEndpointStep with
            {
                Kind = RuntimeExecutionStepKind.RemoveEndpointRoute,
                Identity = "0|1.1.1.0|10",
                DestinationPrefix = "203.0.113.0/24"
            },
            SampleEndpointStep with
            {
                Kind = RuntimeExecutionStepKind.RemoveEndpointRoute,
                Identity = "1|1.1.1.1|11",
                DestinationPrefix = "203.0.113.1/24"
            },
            SampleEndpointStep with
            {
                Kind = RuntimeExecutionStepKind.RemoveEndpointRoute,
                Identity = "2|1.1.1.2|12",
                DestinationPrefix = "203.0.113.2/24"
            }
        ];

        RuntimeExecutionPlan plan = new() { Steps = steps };
        RuntimeExecutionResult result = await executor.ExecuteAsync(plan);

        Assert.Equal(RuntimeExecutionResultStatus.Failed, result.Status);
        Assert.Equal(RuntimeExecutionStepStatus.Failed, result.StepResults[0].Status);
        Assert.Contains(
            result.StepResults.Skip(1),
            sr => sr.Status == RuntimeExecutionStepStatus.Skipped);
    }

    [Fact]
    public async Task Failure_BeforeAnySuccess_ReturnsFailed()
    {
        FakeStepHandler handler = new(succeedAll: true, failOnStep: 0);
        RuntimeExecutor executor = new(handler);

        IReadOnlyList<RuntimeExecutionStep> steps =
        [
            SampleEndpointStep with
            {
                Kind = RuntimeExecutionStepKind.AddEndpointRoute,
                Identity = "fail|1.1.1.1|10"
            }
        ];

        RuntimeExecutionPlan plan = new() { Steps = steps };
        RuntimeExecutionResult result = await executor.ExecuteAsync(plan);

        Assert.Equal(RuntimeExecutionResultStatus.Failed, result.Status);
    }

    [Fact]
    public async Task Failure_AfterSuccess_ReturnsPartiallyCompleted()
    {
        FakeStepHandler handler = new(succeedAll: true, failOnStep: 1);
        RuntimeExecutor executor = new(handler);

        IReadOnlyList<RuntimeExecutionStep> steps =
        [
            SampleEndpointStep with
            {
                Kind = RuntimeExecutionStepKind.AddEndpointRoute,
                Identity = "ok|1.1.1.1|10"
            },
            SampleEndpointStep with
            {
                Kind = RuntimeExecutionStepKind.AddEndpointRoute,
                Identity = "fail|2.2.2.2|20"
            }
        ];

        RuntimeExecutionPlan plan = new() { Steps = steps };
        RuntimeExecutionResult result = await executor.ExecuteAsync(plan);

        Assert.Equal(RuntimeExecutionResultStatus.PartiallyCompleted, result.Status);
    }

    [Fact]
    public async Task Cancellation_AfterSuccess_ReturnsPartiallyCompleted()
    {
        using CancellationTokenSource cts = new();
        FakeStepHandler handler = new(succeedAll: true, cancelOnStep: 1);
        RuntimeExecutor executor = new(handler);

        IReadOnlyList<RuntimeExecutionStep> steps =
        [
            SampleEndpointStep with
            {
                Kind = RuntimeExecutionStepKind.AddEndpointRoute,
                Identity = "ok|1.1.1.1|10"
            },
            SampleEndpointStep with
            {
                Kind = RuntimeExecutionStepKind.AddEndpointRoute,
                Identity = "cancel|2.2.2.2|20"
            }
        ];

        RuntimeExecutionPlan plan = new() { Steps = steps };
        RuntimeExecutionResult result = await executor.ExecuteAsync(plan, cancellationToken: cts.Token);

        Assert.Equal(RuntimeExecutionResultStatus.PartiallyCompleted, result.Status);
    }

    [Fact]
    public async Task Cancellation_StopsNewScheduling()
    {
        using CancellationTokenSource cts = new();
        FakeStepHandler handler = new(succeedAll: true, cancelOnStep: 0);
        RuntimeExecutor executor = new(handler);

        IReadOnlyList<RuntimeExecutionStep> steps =
        [
            SampleEndpointStep with
            {
                Kind = RuntimeExecutionStepKind.AddEndpointRoute,
                Identity = "cancel|1.1.1.1|10"
            },
            SampleEndpointStep with
            {
                Kind = RuntimeExecutionStepKind.AddEndpointRoute,
                Identity = "never|2.2.2.2|20"
            }
        ];

        RuntimeExecutionPlan plan = new() { Steps = steps };

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => executor.ExecuteAsync(plan, cancellationToken: cts.Token));
    }

    [Fact]
    public async Task LaterGroups_DoNotStart_AfterFailure()
    {
        FakeStepHandler handler = new(succeedAll: true, failOnStep: 0);
        RuntimeExecutor executor = new(handler);

        IReadOnlyList<RuntimeExecutionStep> steps =
        [
            SampleEndpointStep with
            {
                Kind = RuntimeExecutionStepKind.AddEndpointRoute,
                Identity = "ep|1.1.1.1|10"
            },
            SampleEndpointStep with
            {
                Kind = RuntimeExecutionStepKind.AddEndpointRoute,
                Identity = "pfx|2.2.2.2|20"
            }
        ];

        RuntimeExecutionPlan plan = new() { Steps = steps };
        RuntimeExecutionResult result = await executor.ExecuteAsync(plan);

        Assert.Equal(RuntimeExecutionResultStatus.Failed, result.Status);
        Assert.Equal(RuntimeExecutionStepStatus.Failed, result.StepResults[0].Status);
        Assert.Equal(RuntimeExecutionStepStatus.Skipped, result.StepResults[1].Status);
    }

    [Fact]
    public async Task EndpointConcurrency_RemainsSequential()
    {
        FakeStepHandler handler = new(succeedAll: true, delayMs: 10);
        RuntimeExecutor executor = new(handler);

        IReadOnlyList<RuntimeExecutionStep> steps = Enumerable.Range(0, 5)
            .Select(i => SampleEndpointStep with
            {
                Kind = RuntimeExecutionStepKind.AddEndpointRoute,
                Identity = $"{i}|1.1.1.{i}|{10 + i}"
            })
            .ToArray();

        Stopwatch sw = Stopwatch.StartNew();
        RuntimeExecutionPlan plan = new() { Steps = steps };
        await executor.ExecuteAsync(plan);
        sw.Stop();

        long minimumMs = 5 * 10;
        Assert.True(sw.ElapsedMilliseconds >= minimumMs * 0.8,
            $"Expected at least {minimumMs}ms for 5 sequential 10ms steps, got {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task PrefixConcurrency_DoesNotExceedMax()
    {
        int maxConcurrent = 0;
        int current = 0;
        object measureLock = new();
        int delayPerStep = 50;

        FakeStepHandler handler = new(delayMs: delayPerStep,
            onStart: _ => { lock (measureLock) { current++; maxConcurrent = Math.Max(maxConcurrent, current); } },
            onEnd: _ => { lock (measureLock) { current--; } });
        RuntimeExecutor executor = new(handler);

        IReadOnlyList<RuntimeExecutionStep> steps = Enumerable.Range(0, 20)
            .Select(i => SamplePrefixStep with
            {
                Kind = RuntimeExecutionStepKind.AddPrefixRoute,
                Identity = $"{i}|1.1.1.{i}|{10 + i}",
                DestinationPrefix = $"203.0.113.{i}/24"
            })
            .ToArray();

        RuntimeExecutionPlan plan = new() { Steps = steps };
        await executor.ExecuteAsync(plan);

        Assert.True(maxConcurrent <= 8,
            $"Expected max concurrency <= 8, got {maxConcurrent}");
    }

    [Fact]
    public async Task Performance_PrefixConcurrencyIsFasterThanSequential()
    {
        int delayPerStep = 30;
        FakeStepHandler handler = new(delayMs: delayPerStep);
        RuntimeExecutor executor = new(handler);

        IReadOnlyList<RuntimeExecutionStep> steps = Enumerable.Range(0, 32)
            .Select(i => SamplePrefixStep with
            {
                Kind = RuntimeExecutionStepKind.AddPrefixRoute,
                Identity = $"{i}|1.1.1.{i}|{10 + i}",
                DestinationPrefix = $"203.0.113.{i}/24"
            })
            .ToArray();

        RuntimeExecutionPlan plan = new() { Steps = steps };
        Stopwatch sw = Stopwatch.StartNew();
        await executor.ExecuteAsync(plan);
        sw.Stop();

        long sequentialMs = 32L * delayPerStep;
        long observedMs = sw.ElapsedMilliseconds;

        Assert.True(observedMs < sequentialMs,
            $"Expected parallel execution ({observedMs}ms) to be faster than sequential ({sequentialMs}ms)");
    }

    [Fact]
    public async Task PrefixGroup_VerificationCalledOnceForAllSteps()
    {
        FakeStepHandler handler = new();
        RuntimeExecutor executor = new(handler);
        RuntimeExecutionPlan plan = CreatePrefixPlan(5);

        RuntimeExecutionResult result = await executor.ExecuteAsync(plan);

        Assert.Equal(RuntimeExecutionResultStatus.Completed, result.Status);
        Assert.Equal(1, handler.VerifyCallCount);
        Assert.Equal(5, handler.VerifiedIdentities.Count);
        for (int i = 0; i < plan.Steps.Count; i++)
            Assert.Equal(plan.Steps[i].Identity, handler.VerifiedIdentities[i]);
    }

    [Fact]
    public async Task PrefixGroup_MixedResults_StopLaterGroups()
    {
        FakeStepHandler handler = new(
            failVerifyIdentity: "203.0.113.1/24|192.168.1.1|11");
        RuntimeExecutor executor = new(handler);

        IReadOnlyList<RuntimeExecutionStep> steps =
        [
            SamplePrefixStep with
            {
                Kind = RuntimeExecutionStepKind.AddPrefixRoute,
                Identity = "203.0.113.0/24|192.168.1.1|10",
                DestinationPrefix = "203.0.113.0/24"
            },
            SamplePrefixStep with
            {
                Kind = RuntimeExecutionStepKind.AddPrefixRoute,
                Identity = "203.0.113.1/24|192.168.1.1|11",
                DestinationPrefix = "203.0.113.1/24"
            },
            SampleEndpointStep with
            {
                Kind = RuntimeExecutionStepKind.RemoveEndpointRoute,
                Identity = "ep|192.168.1.1|10"
            }
        ];

        RuntimeExecutionPlan plan = new() { Steps = steps };
        RuntimeExecutionResult result = await executor.ExecuteAsync(plan);

        Assert.Equal(RuntimeExecutionResultStatus.PartiallyCompleted, result.Status);
        Assert.Equal(RuntimeExecutionStepStatus.Succeeded, result.StepResults[0].Status);
        Assert.Equal(RuntimeExecutionStepStatus.Failed, result.StepResults[1].Status);
        Assert.Equal(RuntimeExecutionStepStatus.Skipped, result.StepResults[2].Status);
    }

    [Fact]
    public async Task PrefixGroup_CancellationMidFlight_NoSuccessResults()
    {
        FakeStepHandler handler = new(succeedAll: true, cancelOnStep: 0);
        RuntimeExecutor executor = new(handler);
        RuntimeExecutionPlan plan = CreatePrefixPlan(3);

        RuntimeExecutionResult result = await executor.ExecuteAsync(plan);

        Assert.Equal(RuntimeExecutionResultStatus.Cancelled, result.Status);
        Assert.DoesNotContain(result.StepResults,
            sr => sr.Status == RuntimeExecutionStepStatus.Succeeded);
        Assert.All(result.StepResults,
            sr => Assert.True(
                sr.Status is RuntimeExecutionStepStatus.Cancelled
                    or RuntimeExecutionStepStatus.Skipped));
    }

    [Fact]
    public async Task HandlerWithoutGroupSupport_FallsBackToPerStepExecution()
    {
        LegacyStepHandler handler = new();
        RuntimeExecutor executor = new(handler);
        RuntimeExecutionPlan plan = CreatePrefixPlan(2);

        RuntimeExecutionResult result = await executor.ExecuteAsync(plan);

        Assert.Equal(RuntimeExecutionResultStatus.Completed, result.Status);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task Progress_ReportsIntermediateUpdates()
    {
        RecordingProgress progress = new();
        FakeStepHandler handler = new(delayMs: 5);
        RuntimeExecutor executor = new(handler);
        RuntimeExecutionPlan plan = CreatePrefixPlan(4);

        RuntimeExecutionResult result = await executor.ExecuteAsync(plan, progress);

        Assert.Equal(RuntimeExecutionResultStatus.Completed, result.Status);
        RuntimeExecutionProgress last = progress.Last;
        Assert.Equal(4, last.TotalSteps);
        Assert.Equal(4, last.ProcessedSteps);
        Assert.Equal(4, last.SucceededSteps);
    }

    [Fact]
    public async Task Progress_Failure_ReportsCorrectCounts()
    {
        RecordingProgress progress = new();
        FakeStepHandler handler = new(succeedAll: true, failOnStep: 1);
        RuntimeExecutor executor = new(handler);
        RuntimeExecutionPlan plan = CreateEndpointPlan(3);

        RuntimeExecutionResult result = await executor.ExecuteAsync(plan, progress);

        Assert.Equal(RuntimeExecutionResultStatus.PartiallyCompleted, result.Status);
        RuntimeExecutionProgress last = progress.Last;
        Assert.Equal(3, last.TotalSteps);
        Assert.Equal(3, last.ProcessedSteps);
        Assert.Equal(1, last.SucceededSteps);
        Assert.Equal(1, last.FailedSteps);
        Assert.Equal(1, last.SkippedSteps);
    }

    [Fact]
    public async Task Progress_Failure_FinalSnapshotIsStableAcrossRuns()
    {
        for (int i = 0; i < 100; i++)
        {
            RecordingProgress progress = new();
            FakeStepHandler handler = new(succeedAll: true, failOnStep: 1);
            RuntimeExecutor executor = new(handler);
            RuntimeExecutionPlan plan = CreateEndpointPlan(3);

            RuntimeExecutionResult result = await executor.ExecuteAsync(plan, progress);

            Assert.Equal(RuntimeExecutionResultStatus.PartiallyCompleted, result.Status);
            RuntimeExecutionProgress last = progress.Last;
            Assert.Equal(3, last.TotalSteps);
            Assert.Equal(3, last.ProcessedSteps);
            Assert.Equal(1, last.SucceededSteps);
            Assert.Equal(1, last.FailedSteps);
            Assert.Equal(0, last.CancelledSteps);
            Assert.Equal(1, last.SkippedSteps);
        }
    }

    private static RuntimeExecutionPlan CreateEndpointPlan(int stepCount)
    {
        return new RuntimeExecutionPlan
        {
            Steps = Enumerable.Range(0, stepCount)
                .Select(i => SampleEndpointStep with
                {
                    Identity = $"10.0.0.{i}/32|192.168.1.1|{10 + i}",
                    DestinationPrefix = $"10.0.0.{i}/32"
                })
                .ToArray()
        };
    }

    private static RuntimeExecutionPlan CreatePrefixPlan(int stepCount)
    {
        return new RuntimeExecutionPlan
        {
            Steps = Enumerable.Range(0, stepCount)
                .Select(i => SamplePrefixStep with
                {
                    Identity = $"203.0.113.{i}/24|192.168.1.1|{10 + i}",
                    DestinationPrefix = $"203.0.113.{i}/24"
                })
                .ToArray()
        };
    }

    private sealed class RecordingProgress
        : IProgress<RuntimeExecutionProgress>
    {
        private readonly object _lock = new();
        private readonly List<RuntimeExecutionProgress> _values = [];

        public void Report(RuntimeExecutionProgress value)
        {
            lock (_lock)
            {
                _values.Add(value);
            }
        }

        public RuntimeExecutionProgress Last
        {
            get
            {
                lock (_lock)
                {
                    return _values[^1];
                }
            }
        }
    }

    private sealed class FakeStepHandler :
        IRuntimeExecutionStepHandler,
        IPrefixGroupExecutionHandler
    {
        private readonly bool _succeedAll;
        private readonly int _failOnStep;
        private readonly int _cancelOnStep;
        private readonly RuntimeExecutionStepResult? _predefinedResult;
        private readonly int _delayMs;
        private readonly List<string>? _trackOrder;
        private readonly object? _callLock;
        private readonly Action<string>? _onStart;
        private readonly Action<string>? _onEnd;
        private readonly object _countLock = new();
        private int _callIndex;

        public int CallCount { get; private set; }
        public int VerifyCallCount { get; private set; }
        public string? FailVerifyIdentity { get; }
        public List<string> ReceivedIdentities { get; } = [];
        public List<string> VerifiedIdentities { get; } = [];

        public FakeStepHandler(
            bool succeedAll = true,
            int failOnStep = -1,
            int cancelOnStep = -1,
            RuntimeExecutionStepResult? predefinedResult = null,
            int delayMs = 0,
            List<string>? trackOrder = null,
            object? callLock = null,
            Action<string>? onStart = null,
            Action<string>? onEnd = null,
            string? failVerifyIdentity = null)
        {
            _succeedAll = succeedAll;
            _failOnStep = failOnStep;
            _cancelOnStep = cancelOnStep;
            _predefinedResult = predefinedResult;
            _delayMs = delayMs;
            _trackOrder = trackOrder;
            _callLock = callLock;
            _onStart = onStart;
            _onEnd = onEnd;
            FailVerifyIdentity = failVerifyIdentity;
        }

        public async Task<PrefixMutationResult> MutatePrefixRouteAsync(
            RuntimeExecutionStep step,
            CancellationToken cancellationToken = default)
        {
            _onStart?.Invoke(step.Identity);

            try
            {
                if (_delayMs > 0)
                    await Task.Delay(_delayMs, cancellationToken);

                cancellationToken.ThrowIfCancellationRequested();

                lock (_countLock)
                {
                    CallCount++;
                    ReceivedIdentities.Add(step.Identity);
                    _trackOrder?.Add(step.Identity);
                }

                int current = _callIndex++;

                if (current == _cancelOnStep)
                    throw new OperationCanceledException(cancellationToken);

                if (current == _failOnStep)
                    return PrefixMutationResult.Failure("Mutation failed.");

                return PrefixMutationResult.Success();
            }
            finally
            {
                _onEnd?.Invoke(step.Identity);
            }
        }

        public Task<IReadOnlyList<RuntimeExecutionStepResult>> VerifyPrefixRouteGroupAsync(
            IReadOnlyList<RuntimeExecutionStep> steps,
            IReadOnlyList<PrefixMutationResult> mutationResults,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            VerifyCallCount++;

            if (_predefinedResult is not null && steps.Count == 1)
            {
                foreach (RuntimeExecutionStep s in steps)
                    VerifiedIdentities.Add(s.Identity);

                return Task.FromResult<IReadOnlyList<RuntimeExecutionStepResult>>(
                    [_predefinedResult]);
            }

            RuntimeExecutionStepResult[] results = new RuntimeExecutionStepResult[steps.Count];
            for (int i = 0; i < steps.Count; i++)
            {
                RuntimeExecutionStep s = steps[i];
                bool failed = FailVerifyIdentity is not null
                              && s.Identity.Equals(
                                  FailVerifyIdentity,
                                  StringComparison.OrdinalIgnoreCase);

                results[i] = new RuntimeExecutionStepResult
                {
                    StepIdentity = s.Identity,
                    Kind = s.Kind,
                    DestinationPrefix = "test",
                    Status = failed
                        ? RuntimeExecutionStepStatus.Failed
                        : RuntimeExecutionStepStatus.Succeeded,
                    ErrorMessage = failed ? "Verified failed." : null
                };
                VerifiedIdentities.Add(s.Identity);
            }

            return Task.FromResult<IReadOnlyList<RuntimeExecutionStepResult>>(results);
        }

        public async Task<RuntimeExecutionStepResult> ExecuteAndVerifyAsync(
            RuntimeExecutionStep step,
            CancellationToken cancellationToken = default)
        {
            _onStart?.Invoke(step.Identity);

            if (_delayMs > 0)
                await Task.Delay(_delayMs, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            lock (_countLock)
            {
                CallCount++;
                ReceivedIdentities.Add(step.Identity);
                _trackOrder?.Add(step.Identity);
            }

            int current = _callIndex++;

            if (_predefinedResult is not null)
            {
                _onEnd?.Invoke(step.Identity);
                return _predefinedResult;
            }

            if (current == _cancelOnStep)
                throw new OperationCanceledException(cancellationToken);

            RuntimeExecutionStepResult result;
            if (current == _failOnStep)
            {
                result = new RuntimeExecutionStepResult
                {
                    StepIdentity = step.Identity,
                    Kind = RuntimeExecutionStepKind.AddPrefixRoute,
                    DestinationPrefix = "test",
                    Status = RuntimeExecutionStepStatus.Failed,
                    ErrorMessage = $"Step {current} failed."
                };
            }
            else
            {
                result = new RuntimeExecutionStepResult
                {
                    StepIdentity = step.Identity,
                    Kind = RuntimeExecutionStepKind.AddPrefixRoute,
                    DestinationPrefix = "test",
                    Status = _succeedAll
                        ? RuntimeExecutionStepStatus.Succeeded
                        : RuntimeExecutionStepStatus.Skipped
                };
            }

            _onEnd?.Invoke(step.Identity);
            return result;
        }
    }

    private sealed class LegacyStepHandler : IRuntimeExecutionStepHandler
    {
        private readonly object _countLock = new();

        public int CallCount { get; private set; }

        public Task<RuntimeExecutionStepResult> ExecuteAndVerifyAsync(
            RuntimeExecutionStep step,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_countLock)
            {
                CallCount++;
            }
            return Task.FromResult(new RuntimeExecutionStepResult
            {
                StepIdentity = step.Identity,
                Kind = step.Kind,
                DestinationPrefix = "test",
                Status = RuntimeExecutionStepStatus.Succeeded
            });
        }
    }
}
