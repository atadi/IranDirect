namespace IranDirect.Core.Runtime.Execution;

public sealed record RuntimeExecutionStepResult
{
    public required string StepIdentity { get; init; }

    public required RuntimeExecutionStepStatus Status
        { get; init; }

    public string? ErrorMessage { get; init; }
}
