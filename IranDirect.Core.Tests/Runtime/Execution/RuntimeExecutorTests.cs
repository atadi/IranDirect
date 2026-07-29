namespace IranDirect.Core.Tests.Runtime.Execution;

using IranDirect.Core.Runtime.Execution;

public sealed class RuntimeExecutorTests
{
    private static readonly RuntimeExecutionStep SampleStep = new()
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
        RuntimeExecutionPlan plan = CreatePlan(3);

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
        RuntimeExecutionPlan plan = CreatePlan(2);

        await executor.ExecuteAsync(plan);

        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task ExecuteAsync_FirstStepFails_ReturnsFailedAndSkipsRemaining()
    {
        FakeStepHandler handler = new(failOnStep: 0);
        RuntimeExecutor executor = new(handler);
        RuntimeExecutionPlan plan = CreatePlan(3);

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
        RuntimeExecutionPlan plan = CreatePlan(3);

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
        RuntimeExecutionPlan plan = CreatePlan(3);

        cts.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => executor.ExecuteAsync(plan, cts.Token));
    }

    [Fact]
    public async Task ExecuteAsync_CancellationAfterSuccess_ReturnsPartiallyCompleted()
    {
        using CancellationTokenSource cts = new();
        FakeStepHandler handler = new(succeedAll: true, cancelOnStep: 1);
        RuntimeExecutor executor = new(handler);
        RuntimeExecutionPlan plan = CreatePlan(3);

        RuntimeExecutionResult result = await executor.ExecuteAsync(
            plan, cts.Token);

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
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task ExecuteAsync_StepsExecutedInPlanOrder()
    {
        FakeStepHandler handler = new();
        RuntimeExecutor executor = new(handler);
        RuntimeExecutionPlan plan = new()
        {
            Steps =
            [
                SampleStep with { Identity = "first|1.1.1.1|1" },
                SampleStep with { Identity = "second|2.2.2.2|2" },
                SampleStep with { Identity = "third|3.3.3.3|3" }
            ]
        };

        await executor.ExecuteAsync(plan);

        Assert.Equal(3, handler.ReceivedIdentities.Count);
        Assert.Equal("first|1.1.1.1|1", handler.ReceivedIdentities[0]);
        Assert.Equal("second|2.2.2.2|2", handler.ReceivedIdentities[1]);
        Assert.Equal("third|3.3.3.3|3", handler.ReceivedIdentities[2]);
    }

    [Fact]
    public async Task ExecuteAsync_ExactStepResultsPreserved()
    {
        RuntimeExecutionStepResult expected = new()
        {
            StepIdentity = SampleStep.Identity,
            Status = RuntimeExecutionStepStatus.Succeeded
        };
        FakeStepHandler handler = new(predefinedResult: expected);
        RuntimeExecutor executor = new(handler);
        RuntimeExecutionPlan plan = CreatePlan(1);

        RuntimeExecutionResult result = await executor.ExecuteAsync(plan);

        Assert.Same(expected, result.StepResults[0]);
    }

    [Fact]
    public async Task ExecuteAsync_MutatedInfrastructure_TrueAfterSuccess()
    {
        FakeStepHandler handler = new();
        RuntimeExecutor executor = new(handler);
        RuntimeExecutionPlan plan = CreatePlan(2);

        RuntimeExecutionResult result = await executor.ExecuteAsync(plan);

        Assert.True(result.MutatedInfrastructure);
    }

    [Fact]
    public async Task ExecuteAsync_MutatedInfrastructure_FalseWhenAllSkipped()
    {
        FakeStepHandler handler = new(succeedAll: false, failOnStep: 0);
        RuntimeExecutor executor = new(handler);
        RuntimeExecutionPlan plan = CreatePlan(2);

        RuntimeExecutionResult result = await executor.ExecuteAsync(plan);

        Assert.False(result.MutatedInfrastructure);
    }

    [Fact]
    public async Task ExecuteAsync_HandlerNotCalledAfterFailure()
    {
        FakeStepHandler handler = new(succeedAll: true, failOnStep: 1);
        RuntimeExecutor executor = new(handler);
        RuntimeExecutionPlan plan = CreatePlan(5);

        await executor.ExecuteAsync(plan);

        Assert.Equal(2, handler.CallCount);
    }

    private static RuntimeExecutionPlan CreatePlan(int stepCount)
    {
        return new RuntimeExecutionPlan
        {
            Steps = Enumerable.Range(0, stepCount)
                .Select(i => SampleStep with
                {
                    Identity = $"203.0.113.{i}/24|192.168.1.1|{10 + i}",
                    DestinationPrefix = $"203.0.113.{i}/24"
                })
                .ToArray()
        };
    }

    private sealed class FakeStepHandler : IRuntimeExecutionStepHandler
    {
        private readonly bool _succeedAll;
        private readonly int _failOnStep;
        private readonly int _cancelOnStep;
        private readonly RuntimeExecutionStepResult? _predefinedResult;
        private int _callIndex;

        public int CallCount { get; private set; }
        public List<string> ReceivedIdentities { get; } = [];

        public FakeStepHandler(
            bool succeedAll = true,
            int failOnStep = -1,
            int cancelOnStep = -1,
            RuntimeExecutionStepResult? predefinedResult = null)
        {
            _succeedAll = succeedAll;
            _failOnStep = failOnStep;
            _cancelOnStep = cancelOnStep;
            _predefinedResult = predefinedResult;
        }

        public Task<RuntimeExecutionStepResult> ExecuteAndVerifyAsync(
            RuntimeExecutionStep step,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            ReceivedIdentities.Add(step.Identity);

            int current = _callIndex++;

            if (_predefinedResult is not null)
                return Task.FromResult(_predefinedResult);

            if (current == _cancelOnStep)
                throw new OperationCanceledException(cancellationToken);

            if (current == _failOnStep)
            {
                return Task.FromResult(new RuntimeExecutionStepResult
                {
                    StepIdentity = step.Identity,
                    Status = RuntimeExecutionStepStatus.Failed,
                    ErrorMessage = $"Step {current} failed."
                });
            }

            return Task.FromResult(new RuntimeExecutionStepResult
            {
                StepIdentity = step.Identity,
                Status = _succeedAll
                    ? RuntimeExecutionStepStatus.Succeeded
                    : RuntimeExecutionStepStatus.Skipped
            });
        }
    }
}
