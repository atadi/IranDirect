namespace PathVeer.Core.Runtime.Execution;

public sealed record RuntimeOperationSnapshot
{
    public OperationState State { get; init; }
    public string? Trigger { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public int PlannedSteps { get; init; }
    public int CompletedSteps { get; init; }
    public int SucceededSteps { get; init; }
    public int FailedSteps { get; init; }
    public int CancelledSteps { get; init; }
    public int SkippedSteps { get; init; }
    public string? ErrorMessage { get; init; }

    public bool IsCompleted => CompletedAt is not null;
}
