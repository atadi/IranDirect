using System.Text.Json.Serialization;

namespace PathVeer.Core.Runtime.Execution;

public sealed record RuntimeExecutionResult
{
    [JsonConstructor]
    private RuntimeExecutionResult() { }

    public RuntimeExecutionResultStatus Status { get; init; }
    public IReadOnlyList<RuntimeExecutionStepResult> StepResults { get; init; } = [];
    public string? ErrorMessage { get; init; }

    public bool MutatedInfrastructure =>
        StepResults.Any(
            sr => sr.Status == RuntimeExecutionStepStatus.Succeeded);

    public static RuntimeExecutionResult NoExecutionRequired() =>
        new()
        {
            Status = RuntimeExecutionResultStatus.NoExecutionRequired
        };

    public static RuntimeExecutionResult Planned() =>
        new()
        {
            Status = RuntimeExecutionResultStatus.Planned
        };

    public static RuntimeExecutionResult Completed(
        IReadOnlyList<RuntimeExecutionStepResult> stepResults)
    {
        if (stepResults.Count == 0)
            throw new ArgumentException(
                "Completed requires at least one step result.",
                nameof(stepResults));

        if (stepResults.Any(
                sr => sr.Status != RuntimeExecutionStepStatus.Succeeded))
            throw new ArgumentException(
                "Completed requires all step results to be Succeeded.",
                nameof(stepResults));

        return new RuntimeExecutionResult
        {
            Status = RuntimeExecutionResultStatus.Completed,
            StepResults = stepResults
        };
    }

    public static RuntimeExecutionResult Failed(
        IReadOnlyList<RuntimeExecutionStepResult> stepResults,
        string? errorMessage = null)
    {
        if (!stepResults.Any(
                sr => sr.Status == RuntimeExecutionStepStatus.Failed))
            throw new ArgumentException(
                "Failed requires at least one Failed step result.",
                nameof(stepResults));

        if (stepResults.Any(
                sr => sr.Status == RuntimeExecutionStepStatus.Succeeded))
            throw new ArgumentException(
                "Failed must not contain Succeeded step results. " +
                "Use PartiallyCompleted instead.",
                nameof(stepResults));

        return new RuntimeExecutionResult
        {
            Status = RuntimeExecutionResultStatus.Failed,
            StepResults = stepResults,
            ErrorMessage = errorMessage
        };
    }

    public static RuntimeExecutionResult Cancelled(
        IReadOnlyList<RuntimeExecutionStepResult> stepResults,
        string? errorMessage = null)
    {
        if (stepResults.Count > 0)
        {
            if (!stepResults.Any(
                    sr => sr.Status == RuntimeExecutionStepStatus.Cancelled))
                throw new ArgumentException(
                    "Cancelled with step results requires " +
                    "at least one Cancelled step result.",
                    nameof(stepResults));

            if (stepResults.Any(
                    sr => sr.Status == RuntimeExecutionStepStatus.Succeeded))
                throw new ArgumentException(
                    "Cancelled must not contain Succeeded step results. " +
                    "Use PartiallyCompleted instead.",
                    nameof(stepResults));
        }

        return new RuntimeExecutionResult
        {
            Status = RuntimeExecutionResultStatus.Cancelled,
            StepResults = stepResults,
            ErrorMessage = errorMessage
        };
    }

    public static RuntimeExecutionResult PartiallyCompleted(
        IReadOnlyList<RuntimeExecutionStepResult> stepResults,
        string? errorMessage = null)
    {
        if (!stepResults.Any(
                sr => sr.Status == RuntimeExecutionStepStatus.Succeeded))
            throw new ArgumentException(
                "PartiallyCompleted requires at least " +
                "one Succeeded step result.",
                nameof(stepResults));

        if (!stepResults.Any(
                sr => sr.Status is RuntimeExecutionStepStatus.Failed
                    or RuntimeExecutionStepStatus.Cancelled
                    or RuntimeExecutionStepStatus.Skipped))
            throw new ArgumentException(
                "PartiallyCompleted requires at least one Failed, " +
                "Cancelled, or Skipped step result.",
                nameof(stepResults));

        return new RuntimeExecutionResult
        {
            Status = RuntimeExecutionResultStatus.PartiallyCompleted,
            StepResults = stepResults,
            ErrorMessage = errorMessage
        };
    }
}
