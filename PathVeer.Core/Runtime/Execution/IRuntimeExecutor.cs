namespace PathVeer.Core.Runtime.Execution;

public interface IRuntimeExecutor
{
    Task<RuntimeExecutionResult> ExecuteAsync(
        RuntimeExecutionPlan plan,
        IProgress<RuntimeExecutionProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
