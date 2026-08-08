namespace PathVeer.Core.Runtime.Execution;

public sealed record RuntimeExecutionStepResult
{
    public required string StepIdentity { get; init; }

    public required RuntimeExecutionStepKind Kind { get; init; }

    public required string DestinationPrefix { get; init; }

    public required RuntimeExecutionStepStatus Status
        { get; init; }

    public string? ErrorMessage { get; init; }
}
