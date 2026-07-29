namespace IranDirect.Core.Runtime.Execution;

public interface IRuntimeExecutionStepHandler
{
    Task<RuntimeExecutionStepResult> ExecuteAndVerifyAsync(
        RuntimeExecutionStep step,
        CancellationToken cancellationToken = default);
}
