namespace PathVeer.Core.Runtime.Execution;

public enum RuntimeExecutionResultStatus
{
    NoExecutionRequired,
    Planned,
    Completed,
    Failed,
    Cancelled,
    PartiallyCompleted
}
