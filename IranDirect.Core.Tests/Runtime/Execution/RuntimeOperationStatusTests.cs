namespace IranDirect.Core.Tests.Runtime.Execution;

using IranDirect.Core.Runtime.Execution;

public sealed class RuntimeOperationStatusTests
{
    [Fact]
    public void Begin_SetsState()
    {
        RuntimeOperationStatus s = new();
        s.Begin(OperationState.Enabling, "user");

        Assert.Equal(OperationState.Enabling, s.State);
        Assert.Equal("user", s.Trigger);
    }

    [Fact]
    public void Begin_ResetsCounters()
    {
        RuntimeOperationStatus s = new();
        s.Begin(OperationState.Enabling, "user");
        ((IProgress<RuntimeExecutionProgress>)s).Report(new RuntimeExecutionProgress(10, 5, 4, 1, 0, 0));
        s.Begin(OperationState.Disabling, "user");

        Assert.Equal(OperationState.Disabling, s.State);
        Assert.Equal(0, s.CompletedSteps);
        Assert.Equal(0, s.SucceededSteps);
    }

    [Fact]
    public void Report_UpdatesProgress()
    {
        RuntimeOperationStatus s = new();
        s.Begin(OperationState.Enabling, "user");

        ((IProgress<RuntimeExecutionProgress>)s).Report(new RuntimeExecutionProgress(10, 3, 2, 1, 0, 0));

        Assert.Equal(3, s.CompletedSteps);
        Assert.Equal(2, s.SucceededSteps);
        Assert.Equal(1, s.FailedSteps);
    }

    [Fact]
    public void Complete_CompletedExecution_SetsIdle()
    {
        RuntimeOperationStatus s = new();
        s.Begin(OperationState.Enabling, "user");
        s.SetPlannedSteps(2);

        RuntimeExecutionResult result = RuntimeExecutionResult.Completed([
            new RuntimeExecutionStepResult
            {
                StepIdentity = "a", Status = RuntimeExecutionStepStatus.Succeeded
            },
            new RuntimeExecutionStepResult
            {
                StepIdentity = "b", Status = RuntimeExecutionStepStatus.Succeeded
            }
        ]);
        s.Complete(result);

        Assert.Equal(OperationState.Idle, s.State);
        Assert.Equal(2, s.SucceededSteps);
        Assert.NotNull(s.CompletedAt);
    }

    [Fact]
    public void Complete_FailedExecution_SetsFailed()
    {
        RuntimeOperationStatus s = new();
        s.Begin(OperationState.Enabling, "user");
        s.SetPlannedSteps(2);

        RuntimeExecutionResult result = RuntimeExecutionResult.Failed([
            new RuntimeExecutionStepResult
            {
                StepIdentity = "a", Status = RuntimeExecutionStepStatus.Failed, ErrorMessage = "err"
            },
            new RuntimeExecutionStepResult
            {
                StepIdentity = "b", Status = RuntimeExecutionStepStatus.Skipped
            }
        ], "err");
        s.Complete(result);

        Assert.Equal(OperationState.Failed, s.State);
        Assert.Equal("err", s.ErrorMessage);
    }

    [Fact]
    public void Complete_PartiallyCompleted_SetsFailed()
    {
        RuntimeOperationStatus s = new();
        s.Begin(OperationState.Enabling, "user");
        s.SetPlannedSteps(2);

        RuntimeExecutionResult result = RuntimeExecutionResult.PartiallyCompleted([
            new RuntimeExecutionStepResult
            {
                StepIdentity = "a", Status = RuntimeExecutionStepStatus.Succeeded
            },
            new RuntimeExecutionStepResult
            {
                StepIdentity = "b", Status = RuntimeExecutionStepStatus.Failed, ErrorMessage = "partial"
            }
        ], "partial");
        s.Complete(result);

        Assert.Equal(OperationState.Failed, s.State);
    }

    [Fact]
    public void Fail_SetsFailedState()
    {
        RuntimeOperationStatus s = new();
        s.Begin(OperationState.Enabling, "user");
        s.Fail("something went wrong");

        Assert.Equal(OperationState.Failed, s.State);
        Assert.Equal("something went wrong", s.ErrorMessage);
        Assert.NotNull(s.CompletedAt);
    }

    [Fact]
    public void Constructor_StartsIdle()
    {
        RuntimeOperationStatus s = new();
        Assert.Equal(OperationState.Idle, s.State);
        Assert.Null(s.ErrorMessage);
    }
}
