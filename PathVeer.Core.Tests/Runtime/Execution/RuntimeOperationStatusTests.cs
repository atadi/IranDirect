namespace PathVeer.Core.Tests.Runtime.Execution;

using PathVeer.Core.Runtime.Execution;

public sealed class RuntimeOperationStatusTests
{
    [Fact]
    public void Constructor_StartsIdle()
    {
        RuntimeOperationStatus s = new();
        Assert.Equal(OperationState.Idle, s.State);
        Assert.Null(s.ErrorMessage);
    }

    [Fact]
    public void Constructor_IsCompletedFalse()
    {
        RuntimeOperationStatus s = new();
        RuntimeOperationSnapshot snap = s.CreateSnapshot();
        Assert.False(snap.IsCompleted);
    }

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
    public void Begin_AfterCompletion_ResetsCountersAndCompletedAt()
    {
        RuntimeOperationStatus s = new();
        s.Begin(OperationState.Enabling, "user");
        s.SetPlannedSteps(2);
        s.Complete(RuntimeExecutionResult.Completed([
            new RuntimeExecutionStepResult
            {
                StepIdentity = "a",
                Kind = RuntimeExecutionStepKind.AddPrefixRoute,
                DestinationPrefix = "test",
                Status = RuntimeExecutionStepStatus.Succeeded
            },
            new RuntimeExecutionStepResult
            {
                StepIdentity = "b",
                Kind = RuntimeExecutionStepKind.AddPrefixRoute,
                DestinationPrefix = "test",
                Status = RuntimeExecutionStepStatus.Succeeded
            }
        ]));

        RuntimeOperationSnapshot before = s.CreateSnapshot();
        Assert.True(before.IsCompleted);
        Assert.NotNull(before.CompletedAt);

        s.Begin(OperationState.Disabling, "cycle");

        RuntimeOperationSnapshot after = s.CreateSnapshot();
        Assert.Equal(OperationState.Disabling, after.State);
        Assert.Equal("cycle", after.Trigger);
        Assert.Null(after.CompletedAt);
        Assert.False(after.IsCompleted);
        Assert.Equal(0, after.PlannedSteps);
        Assert.Equal(0, after.CompletedSteps);
        Assert.Equal(0, after.SucceededSteps);
        Assert.Equal(0, after.FailedSteps);
        Assert.Equal(0, after.CancelledSteps);
        Assert.Equal(0, after.SkippedSteps);
        Assert.Null(after.ErrorMessage);
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
    public void Complete_CompletedExecution_PreservesState()
    {
        RuntimeOperationStatus s = new();
        s.Begin(OperationState.Enabling, "user");
        s.SetPlannedSteps(2);

        RuntimeExecutionResult result = RuntimeExecutionResult.Completed([
            new RuntimeExecutionStepResult
            {
                StepIdentity = "a",
                Kind = RuntimeExecutionStepKind.AddPrefixRoute,
                DestinationPrefix = "test",
                Status = RuntimeExecutionStepStatus.Succeeded
            },
            new RuntimeExecutionStepResult
            {
                StepIdentity = "b",
                Kind = RuntimeExecutionStepKind.AddPrefixRoute,
                DestinationPrefix = "test",
                Status = RuntimeExecutionStepStatus.Succeeded
            }
        ]);
        s.Complete(result);

        Assert.Equal(OperationState.Enabling, s.State);
        Assert.Equal(2, s.SucceededSteps);
        Assert.NotNull(s.CompletedAt);

        RuntimeOperationSnapshot snap = s.CreateSnapshot();
        Assert.True(snap.IsCompleted);
        Assert.NotNull(snap.CompletedAt);
    }

    [Fact]
    public void Complete_CompletedEnabling_PreservesEnabling()
    {
        RuntimeOperationStatus s = new();
        s.Begin(OperationState.Enabling, "user");
        s.SetPlannedSteps(1);
        s.Complete(RuntimeExecutionResult.Completed([
            new RuntimeExecutionStepResult
            {
                StepIdentity = "a",
                Kind = RuntimeExecutionStepKind.AddPrefixRoute,
                DestinationPrefix = "test",
                Status = RuntimeExecutionStepStatus.Succeeded
            }
        ]));

        Assert.Equal(OperationState.Enabling, s.State);
    }

    [Fact]
    public void Complete_CompletedDisabling_PreservesDisabling()
    {
        RuntimeOperationStatus s = new();
        s.Begin(OperationState.Disabling, "user");
        s.SetPlannedSteps(1);
        s.Complete(RuntimeExecutionResult.Completed([
            new RuntimeExecutionStepResult
            {
                StepIdentity = "a",
                Kind = RuntimeExecutionStepKind.AddPrefixRoute,
                DestinationPrefix = "test",
                Status = RuntimeExecutionStepStatus.Succeeded
            }
        ]));

        Assert.Equal(OperationState.Disabling, s.State);
    }

    [Fact]
    public void Complete_CompletedRepairing_PreservesRepairing()
    {
        RuntimeOperationStatus s = new();
        s.Begin(OperationState.Repairing, "cycle");
        s.SetPlannedSteps(1);
        s.Complete(RuntimeExecutionResult.Completed([
            new RuntimeExecutionStepResult
            {
                StepIdentity = "a",
                Kind = RuntimeExecutionStepKind.AddPrefixRoute,
                DestinationPrefix = "test",
                Status = RuntimeExecutionStepStatus.Succeeded
            }
        ]));

        Assert.Equal(OperationState.Repairing, s.State);
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
                StepIdentity = "a",
                Kind = RuntimeExecutionStepKind.AddPrefixRoute,
                DestinationPrefix = "test",
                Status = RuntimeExecutionStepStatus.Failed, ErrorMessage = "err"
            },
            new RuntimeExecutionStepResult
            {
                StepIdentity = "b",
                Kind = RuntimeExecutionStepKind.AddPrefixRoute,
                DestinationPrefix = "test",
                Status = RuntimeExecutionStepStatus.Skipped
            }
        ], "err");
        s.Complete(result);

        Assert.Equal(OperationState.Failed, s.State);
        Assert.Equal("err", s.ErrorMessage);

        RuntimeOperationSnapshot snap = s.CreateSnapshot();
        Assert.Equal(OperationState.Failed, snap.State);
        Assert.True(snap.IsCompleted);
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
                StepIdentity = "a",
                Kind = RuntimeExecutionStepKind.AddPrefixRoute,
                DestinationPrefix = "test",
                Status = RuntimeExecutionStepStatus.Succeeded
            },
            new RuntimeExecutionStepResult
            {
                StepIdentity = "b",
                Kind = RuntimeExecutionStepKind.AddPrefixRoute,
                DestinationPrefix = "test",
                Status = RuntimeExecutionStepStatus.Failed, ErrorMessage = "partial"
            }
        ], "partial");
        s.Complete(result);

        Assert.Equal(OperationState.Failed, s.State);

        RuntimeOperationSnapshot snap = s.CreateSnapshot();
        Assert.True(snap.IsCompleted);
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

        RuntimeOperationSnapshot snap = s.CreateSnapshot();
        Assert.True(snap.IsCompleted);
    }

    [Fact]
    public void CreateSnapshot_ReturnsSeparateObject()
    {
        RuntimeOperationStatus s = new();
        s.Begin(OperationState.Enabling, "user");

        RuntimeOperationSnapshot snap = s.CreateSnapshot();

        Assert.Equal(OperationState.Enabling, snap.State);
        Assert.Equal("user", snap.Trigger);
        Assert.Equal(s.StartedAt, snap.StartedAt);
    }

    [Fact]
    public void CreateSnapshot_IsImmutableAfterMutation()
    {
        RuntimeOperationStatus s = new();
        s.Begin(OperationState.Enabling, "user");
        s.SetPlannedSteps(5);

        RuntimeOperationSnapshot snap = s.CreateSnapshot();
        Assert.Equal(5, snap.PlannedSteps);

        s.SetPlannedSteps(10);

        Assert.Equal(5, snap.PlannedSteps);
        Assert.Equal(10, s.PlannedSteps);
    }

    [Fact]
    public void CreateSnapshot_SnapshotNotAffectedBySubsequentBegin()
    {
        RuntimeOperationStatus s = new();
        s.Begin(OperationState.Enabling, "user");
        s.SetPlannedSteps(3);

        RuntimeOperationSnapshot snap = s.CreateSnapshot();

        s.Begin(OperationState.Disabling, "other");

        Assert.Equal(OperationState.Enabling, snap.State);
        Assert.Equal("user", snap.Trigger);
        Assert.Equal(3, snap.PlannedSteps);
    }

    [Fact]
    public void CreateSnapshot_InitialState_IsNotCompleted()
    {
        RuntimeOperationStatus s = new();
        RuntimeOperationSnapshot snap = s.CreateSnapshot();

        Assert.Equal(OperationState.Idle, snap.State);
        Assert.False(snap.IsCompleted);
        Assert.Null(snap.CompletedAt);
        Assert.Null(snap.Trigger);
    }

    [Fact]
    public void SuccessfulCompletion_SetsCompletedAtAndIsCompleted()
    {
        RuntimeOperationStatus s = new();
        s.Begin(OperationState.Enabling, "user");
        s.SetPlannedSteps(1);
        s.Complete(RuntimeExecutionResult.Completed([
            new RuntimeExecutionStepResult
            {
                StepIdentity = "a",
                Kind = RuntimeExecutionStepKind.AddPrefixRoute,
                DestinationPrefix = "test",
                Status = RuntimeExecutionStepStatus.Succeeded
            }
        ]));

        RuntimeOperationSnapshot snap = s.CreateSnapshot();
        Assert.True(snap.IsCompleted);
        Assert.NotNull(snap.CompletedAt);
        Assert.Equal(OperationState.Enabling, snap.State);
    }
}
