namespace IranDirect.Core.Runtime.Execution;

public sealed record RuntimeExecutionProgress(
    int TotalSteps,
    int ProcessedSteps,
    int SucceededSteps,
    int FailedSteps,
    int CancelledSteps,
    int SkippedSteps);
