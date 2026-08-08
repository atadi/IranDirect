namespace PathVeer.Core.Runtime;

using PathVeer.Core.Runtime.Execution;

public sealed record RuntimeCycleExecutionResult
{
    public required RuntimeDecision Decision { get; init; }
    public required RuntimeExecutionResult Execution { get; init; }

    public bool IsSuccess => Execution.Status is
        RuntimeExecutionResultStatus.Completed or
        RuntimeExecutionResultStatus.NoExecutionRequired;
}
