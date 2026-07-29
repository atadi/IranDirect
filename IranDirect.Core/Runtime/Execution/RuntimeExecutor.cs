namespace IranDirect.Core.Runtime.Execution;

public sealed class RuntimeExecutor : IRuntimeExecutor
{
    private const int DefaultMaxDegreeOfParallelism = 8;
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

        RuntimeExecutionStepResult[] results = new RuntimeExecutionStepResult[plan.Steps.Count];
        bool hasSuccess = false;

        (bool stopped, hasSuccess) = await ExecuteSequentialGroupAsync(
            plan.Steps, RuntimeExecutionStepKind.AddEndpointRoute, results, hasSuccess, cancellationToken);
        if (stopped)
        {
            FillNullResults(plan.Steps, results);
            return BuildFinalResult(results, hasSuccess);
        }

        (stopped, hasSuccess) = await ExecuteBoundedGroupAsync(
            plan.Steps, RuntimeExecutionStepKind.RemovePrefixRoute, results, hasSuccess, DefaultMaxDegreeOfParallelism, cancellationToken);
        if (stopped)
        {
            FillNullResults(plan.Steps, results);
            return BuildFinalResult(results, hasSuccess);
        }

        (stopped, hasSuccess) = await ExecuteBoundedGroupAsync(
            plan.Steps, RuntimeExecutionStepKind.AddPrefixRoute, results, hasSuccess, DefaultMaxDegreeOfParallelism, cancellationToken);
        if (stopped)
        {
            FillNullResults(plan.Steps, results);
            return BuildFinalResult(results, hasSuccess);
        }

        (stopped, hasSuccess) = await ExecuteSequentialGroupAsync(
            plan.Steps, RuntimeExecutionStepKind.RemoveEndpointRoute, results, hasSuccess, cancellationToken);
        if (stopped)
        {
            FillNullResults(plan.Steps, results);
            return BuildFinalResult(results, hasSuccess);
        }

        return BuildFinalResult(results, hasSuccess);
    }

    private async Task<(bool Stopped, bool HasSuccess)> ExecuteSequentialGroupAsync(
        IReadOnlyList<RuntimeExecutionStep> allSteps,
        RuntimeExecutionStepKind kind,
        RuntimeExecutionStepResult[] results,
        bool hasSuccess,
        CancellationToken cancellationToken)
    {
        for (int i = 0; i < allSteps.Count; i++)
        {
            if (allSteps[i].Kind != kind)
                continue;

            cancellationToken.ThrowIfCancellationRequested();

            RuntimeExecutionStepResult stepResult;
            try
            {
                stepResult = await _handler.ExecuteAndVerifyAsync(
                    allSteps[i], cancellationToken);
            }
            catch (OperationCanceledException)
            {
                results[i] = CreateStepResult(allSteps[i].Identity, RuntimeExecutionStepStatus.Cancelled);
                if (hasSuccess)
                    return (true, hasSuccess);

                throw;
            }

            results[i] = stepResult;

            if (stepResult.Status == RuntimeExecutionStepStatus.Succeeded)
            {
                hasSuccess = true;
            }
            else if (stepResult.Status is RuntimeExecutionStepStatus.Failed
                     or RuntimeExecutionStepStatus.Cancelled)
            {
                return (true, hasSuccess);
            }
        }

        return (false, hasSuccess);
    }

    private async Task<(bool Stopped, bool HasSuccess)> ExecuteBoundedGroupAsync(
        IReadOnlyList<RuntimeExecutionStep> allSteps,
        RuntimeExecutionStepKind kind,
        RuntimeExecutionStepResult[] results,
        bool hasSuccess,
        int maxDop,
        CancellationToken cancellationToken)
    {
        int[] indices = Enumerable.Range(0, allSteps.Count)
            .Where(i => allSteps[i].Kind == kind)
            .ToArray();

        if (indices.Length == 0)
            return (false, hasSuccess);

        using SemaphoreSlim throttle = new(maxDop, maxDop);
        List<Task> running = new(indices.Length);
        bool groupFailed = false;
        bool anyStarted = false;

        for (int t = 0; t < indices.Length; t++)
        {
            int idx = indices[t];

            if (Volatile.Read(ref groupFailed))
            {
                results[idx] = CreateStepResult(allSteps[idx].Identity, RuntimeExecutionStepStatus.Skipped);
                continue;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                if (!anyStarted)
                    cancellationToken.ThrowIfCancellationRequested();

                results[idx] = CreateStepResult(allSteps[idx].Identity, RuntimeExecutionStepStatus.Skipped);
                continue;
            }

            try
            {
                await throttle.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                if (!anyStarted)
                    throw;

                results[idx] = CreateStepResult(allSteps[idx].Identity, RuntimeExecutionStepStatus.Cancelled);
                for (int r = t + 1; r < indices.Length; r++)
                    results[indices[r]] = CreateStepResult(allSteps[indices[r]].Identity, RuntimeExecutionStepStatus.Skipped);
                Volatile.Write(ref groupFailed, true);
                break;
            }

            if (Volatile.Read(ref groupFailed))
            {
                throttle.Release();
                results[idx] = CreateStepResult(allSteps[idx].Identity, RuntimeExecutionStepStatus.Skipped);
                continue;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                throttle.Release();
                if (!anyStarted)
                    cancellationToken.ThrowIfCancellationRequested();

                results[idx] = CreateStepResult(allSteps[idx].Identity, RuntimeExecutionStepStatus.Skipped);
                continue;
            }

            anyStarted = true;
            int captured = idx;
            running.Add(Task.Run(async () =>
            {
                RuntimeExecutionStepResult r;
                try
                {
                    r = await _handler.ExecuteAndVerifyAsync(allSteps[captured], cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    r = CreateStepResult(allSteps[captured].Identity, RuntimeExecutionStepStatus.Cancelled);
                }

                if (r.Status is RuntimeExecutionStepStatus.Failed or RuntimeExecutionStepStatus.Cancelled)
                    Volatile.Write(ref groupFailed, true);

                results[captured] = r;
                throttle.Release();
            }, CancellationToken.None));
        }

        try
        {
            await Task.WhenAll(running);
        }
        catch (OperationCanceledException)
        {
        }

        bool groupHasSuccess = false;
        foreach (RuntimeExecutionStepResult? r in results)
        {
            if (r?.Status == RuntimeExecutionStepStatus.Succeeded)
                groupHasSuccess = true;
        }

        return (Volatile.Read(ref groupFailed), hasSuccess || groupHasSuccess);
    }

    private static void FillNullResults(
        IReadOnlyList<RuntimeExecutionStep> allSteps,
        RuntimeExecutionStepResult[] results)
    {
        for (int i = 0; i < results.Length; i++)
        {
            if (results[i] is null)
                results[i] = CreateStepResult(allSteps[i].Identity, RuntimeExecutionStepStatus.Skipped);
        }
    }

    private static RuntimeExecutionResult BuildFinalResult(
        RuntimeExecutionStepResult[] results,
        bool hasSuccess)
    {
        IReadOnlyList<RuntimeExecutionStepResult> finalResults = results.ToArray();
        bool hasFailure = results.Any(r => r.Status == RuntimeExecutionStepStatus.Failed);
        bool hasCancelled = results.Any(r => r.Status == RuntimeExecutionStepStatus.Cancelled);

        if (hasFailure && !hasSuccess)
            return RuntimeExecutionResult.Failed(finalResults);

        if (hasCancelled && !hasSuccess)
            return RuntimeExecutionResult.Cancelled(finalResults);

        if (hasSuccess && (hasFailure || hasCancelled))
            return RuntimeExecutionResult.PartiallyCompleted(finalResults);

        return RuntimeExecutionResult.Completed(finalResults);
    }

    private static RuntimeExecutionStepResult CreateStepResult(
        string identity, RuntimeExecutionStepStatus status) =>
        new()
        {
            StepIdentity = identity,
            Status = status
        };
}
