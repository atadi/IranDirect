namespace IranDirect.Core.Runtime.Execution;

public sealed class RuntimeExecutor : IRuntimeExecutor
{
    private readonly IRuntimeExecutionStepHandler _handler;

    public RuntimeExecutor(
        IRuntimeExecutionStepHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        _handler = handler;
    }

    public async Task<RuntimeExecutionResult> ExecuteAsync(
        RuntimeExecutionPlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (plan.IsEmpty)
            return RuntimeExecutionResult.NoExecutionRequired();

        List<RuntimeExecutionStepResult> results = [];
        bool hasSuccess = false;

        for (int i = 0; i < plan.Steps.Count; i++)
        {
            RuntimeExecutionStep step = plan.Steps[i];

            cancellationToken.ThrowIfCancellationRequested();

            RuntimeExecutionStepResult stepResult;
            try
            {
                stepResult = await _handler.ExecuteAndVerifyAsync(
                    step, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                if (hasSuccess)
                {
                    results.Add(new RuntimeExecutionStepResult
                    {
                        StepIdentity = step.Identity,
                        Status = RuntimeExecutionStepStatus.Cancelled
                    });
                    AppendSkipped(i, plan.Steps, results);
                    return RuntimeExecutionResult.PartiallyCompleted(
                        results, "Execution cancelled after one or more steps completed.");
                }

                throw;
            }

            results.Add(stepResult);

            if (stepResult.Status == RuntimeExecutionStepStatus.Succeeded)
            {
                hasSuccess = true;
            }
            else if (stepResult.Status is RuntimeExecutionStepStatus.Failed
                     or RuntimeExecutionStepStatus.Cancelled)
            {
                AppendSkipped(i, plan.Steps, results);
                return ClassifyTerminalResult(results, hasSuccess, stepResult);
            }
        }

        return RuntimeExecutionResult.Completed(results);
    }

    private static void AppendSkipped(
        int currentIndex,
        IReadOnlyList<RuntimeExecutionStep> allSteps,
        List<RuntimeExecutionStepResult> results)
    {
        for (int i = currentIndex + 1; i < allSteps.Count; i++)
        {
            results.Add(new RuntimeExecutionStepResult
            {
                StepIdentity = allSteps[i].Identity,
                Status = RuntimeExecutionStepStatus.Skipped
            });
        }
    }

    private static RuntimeExecutionResult ClassifyTerminalResult(
        List<RuntimeExecutionStepResult> results,
        bool hasSuccess,
        RuntimeExecutionStepResult stepResult)
    {
        if (hasSuccess)
        {
            return RuntimeExecutionResult.PartiallyCompleted(
                results, stepResult.ErrorMessage);
        }

        if (stepResult.Status == RuntimeExecutionStepStatus.Cancelled)
        {
            return RuntimeExecutionResult.Cancelled(
                results, stepResult.ErrorMessage);
        }

        return RuntimeExecutionResult.Failed(
            results, stepResult.ErrorMessage);
    }
}
