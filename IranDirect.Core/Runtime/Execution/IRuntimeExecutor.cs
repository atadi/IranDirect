namespace IranDirect.Core.Runtime.Execution;

public interface IRuntimeExecutor
{
    Task<RuntimeExecutionResult> ExecuteAsync(
        RuntimeExecutionPlan plan,
        CancellationToken cancellationToken = default);
}
