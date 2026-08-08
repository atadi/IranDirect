namespace PathVeer.Core.Runtime.Execution;

using System.Threading;

public enum OperationState
{
    Idle,
    Enabling,
    Disabling,
    Repairing,
    Failed
}

public sealed class RuntimeOperationStatus : IProgress<RuntimeExecutionProgress>
{
    private readonly object _lock = new();

    public OperationState State { get; private set; }
    public string? Trigger { get; private set; }
    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public int PlannedSteps { get; private set; }
    public int CompletedSteps { get; private set; }
    public int SucceededSteps { get; private set; }
    public int FailedSteps { get; private set; }
    public int CancelledSteps { get; private set; }
    public int SkippedSteps { get; private set; }
    public string? ErrorMessage { get; private set; }

    public RuntimeOperationStatus()
    {
        State = OperationState.Idle;
    }

    public void Begin(OperationState state, string? trigger = null)
    {
        lock (_lock)
        {
            State = state;
            Trigger = trigger;
            StartedAt = DateTimeOffset.UtcNow;
            CompletedAt = null;
            PlannedSteps = 0;
            CompletedSteps = 0;
            SucceededSteps = 0;
            FailedSteps = 0;
            CancelledSteps = 0;
            SkippedSteps = 0;
            ErrorMessage = null;
        }
    }

    public void SetPlannedSteps(int count)
    {
        lock (_lock)
        {
            PlannedSteps = count;
        }
    }

    void IProgress<RuntimeExecutionProgress>.Report(RuntimeExecutionProgress value)
    {
        lock (_lock)
        {
            CompletedSteps = value.ProcessedSteps;
            SucceededSteps = value.SucceededSteps;
            FailedSteps = value.FailedSteps;
            CancelledSteps = value.CancelledSteps;
            SkippedSteps = value.SkippedSteps;
        }
    }

    public void Complete(RuntimeExecutionResult result)
    {
        lock (_lock)
        {
            CompletedAt = DateTimeOffset.UtcNow;
            CompletedSteps = PlannedSteps;

            var steps = result.StepResults;
            SucceededSteps = steps.Count(s => s.Status == RuntimeExecutionStepStatus.Succeeded);
            FailedSteps = steps.Count(s => s.Status == RuntimeExecutionStepStatus.Failed);
            CancelledSteps = steps.Count(s => s.Status == RuntimeExecutionStepStatus.Cancelled);
            SkippedSteps = steps.Count(s => s.Status == RuntimeExecutionStepStatus.Skipped);

            if (result.Status is RuntimeExecutionResultStatus.Failed
                or RuntimeExecutionResultStatus.PartiallyCompleted)
            {
                State = OperationState.Failed;
                ErrorMessage = result.ErrorMessage ?? "Execution completed with errors.";
            }
        }
    }

    public RuntimeOperationSnapshot CreateSnapshot()
    {
        lock (_lock)
        {
            return new RuntimeOperationSnapshot
            {
                State = State,
                Trigger = Trigger,
                StartedAt = StartedAt,
                CompletedAt = CompletedAt,
                PlannedSteps = PlannedSteps,
                CompletedSteps = CompletedSteps,
                SucceededSteps = SucceededSteps,
                FailedSteps = FailedSteps,
                CancelledSteps = CancelledSteps,
                SkippedSteps = SkippedSteps,
                ErrorMessage = ErrorMessage
            };
        }
    }

    public void Fail(string error)
    {
        lock (_lock)
        {
            CompletedAt = DateTimeOffset.UtcNow;
            State = OperationState.Failed;
            ErrorMessage = error;
        }
    }
}
