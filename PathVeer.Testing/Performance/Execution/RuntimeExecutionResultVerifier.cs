using PathVeer.Core.Runtime.Execution;

namespace PathVeer.Testing.Performance.Execution;

public static class RuntimeExecutionResultVerifier
{
    public static void VerifyPlanOrder(
        RuntimeExecutionPlan plan,
        RuntimeExecutionResult result)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(result);

        if (result.StepResults.Count != plan.Count)
        {
            throw new InvalidOperationException(
                $"Expected {plan.Count} step results, got {result.StepResults.Count}.");
        }

        for (int i = 0; i < plan.Count; i++)
        {
            RuntimeExecutionStepResult stepResult = result.StepResults[i];
            if (!string.Equals(
                    stepResult.StepIdentity,
                    plan.Steps[i].Identity,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Step result at index {i} is for '{stepResult.StepIdentity}' " +
                    $"but the plan expects '{plan.Steps[i].Identity}'.");
            }
        }
    }

    public static void VerifyFinalProgressStable(
        RuntimeExecutionResult result,
        IReadOnlyList<RuntimeExecutionProgress> snapshots)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(snapshots);

        if (snapshots.Count == 0)
        {
            throw new InvalidOperationException(
                "Expected at least one progress snapshot.");
        }

        RuntimeExecutionProgress last = snapshots[^1];

        if (last.TotalSteps != result.StepResults.Count)
        {
            throw new InvalidOperationException(
                $"Final progress TotalSteps is {last.TotalSteps} but the result has " +
                $"{result.StepResults.Count} steps.");
        }

        if (last.ProcessedSteps != result.StepResults.Count)
        {
            throw new InvalidOperationException(
                $"Final progress ProcessedSteps is {last.ProcessedSteps} but the result has " +
                $"{result.StepResults.Count} steps.");
        }

        int succeeded = result.StepResults.Count(
            sr => sr.Status == RuntimeExecutionStepStatus.Succeeded);
        int failed = result.StepResults.Count(
            sr => sr.Status == RuntimeExecutionStepStatus.Failed);
        int cancelled = result.StepResults.Count(
            sr => sr.Status == RuntimeExecutionStepStatus.Cancelled);
        int skipped = result.StepResults.Count(
            sr => sr.Status == RuntimeExecutionStepStatus.Skipped);

        if (last.SucceededSteps != succeeded ||
            last.FailedSteps != failed ||
            last.CancelledSteps != cancelled ||
            last.SkippedSteps != skipped)
        {
            throw new InvalidOperationException(
                $"Final progress counters " +
                $"({last.SucceededSteps} succeeded, {last.FailedSteps} failed, " +
                $"{last.CancelledSteps} cancelled, {last.SkippedSteps} skipped) do not match " +
                $"the step results ({succeeded} succeeded, {failed} failed, " +
                $"{cancelled} cancelled, {skipped} skipped).");
        }

        int processed = 0;
        foreach (RuntimeExecutionProgress snapshot in snapshots)
        {
            if (snapshot.ProcessedSteps < processed)
            {
                throw new InvalidOperationException(
                    $"Progress ProcessedSteps went backwards from {processed} to " +
                    $"{snapshot.ProcessedSteps}.");
            }

            if (snapshot.ProcessedSteps > snapshot.TotalSteps)
            {
                throw new InvalidOperationException(
                    $"Progress ProcessedSteps {snapshot.ProcessedSteps} exceeds " +
                    $"TotalSteps {snapshot.TotalSteps}.");
            }

            processed = snapshot.ProcessedSteps;
        }
    }

    public static void VerifyStatusCounts(
        RuntimeExecutionResult result,
        int succeeded,
        int failed,
        int cancelled,
        int skipped)
    {
        ArgumentNullException.ThrowIfNull(result);

        int actualSucceeded = result.StepResults.Count(
            sr => sr.Status == RuntimeExecutionStepStatus.Succeeded);
        int actualFailed = result.StepResults.Count(
            sr => sr.Status == RuntimeExecutionStepStatus.Failed);
        int actualCancelled = result.StepResults.Count(
            sr => sr.Status == RuntimeExecutionStepStatus.Cancelled);
        int actualSkipped = result.StepResults.Count(
            sr => sr.Status == RuntimeExecutionStepStatus.Skipped);

        if (actualSucceeded != succeeded ||
            actualFailed != failed ||
            actualCancelled != cancelled ||
            actualSkipped != skipped)
        {
            throw new InvalidOperationException(
                $"Expected {succeeded} succeeded, {failed} failed, {cancelled} cancelled, " +
                $"{skipped} skipped but got {actualSucceeded} succeeded, {actualFailed} failed, " +
                $"{actualCancelled} cancelled, {actualSkipped} skipped.");
        }
    }
}
