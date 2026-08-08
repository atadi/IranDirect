namespace PathVeer.Core.Runtime.Execution;

public sealed class RuntimeExecutor : IRuntimeExecutor
{
    private const int DefaultMaxDegreeOfParallelism = 8;
    private readonly IRuntimeExecutionStepHandler _handler;
    private readonly IPrefixGroupExecutionHandler? _prefixGroupHandler;

    public RuntimeExecutor(
        IRuntimeExecutionStepHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        _handler = handler;
        _prefixGroupHandler = handler as IPrefixGroupExecutionHandler;
    }

    public async Task<RuntimeExecutionResult> ExecuteAsync(
        RuntimeExecutionPlan plan,
        IProgress<RuntimeExecutionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (plan.IsEmpty)
            return RuntimeExecutionResult.NoExecutionRequired();

        RuntimeExecutionStepResult[] results = new RuntimeExecutionStepResult[plan.Steps.Count];
        bool hasSuccess = false;

        int processed = 0, succeeded = 0, failed = 0, cancelled = 0, skipped = 0;

        void UpdateProgress(RuntimeExecutionStepStatus status)
        {
            processed++;
            switch (status)
            {
                case RuntimeExecutionStepStatus.Succeeded: succeeded++; break;
                case RuntimeExecutionStepStatus.Failed: failed++; break;
                case RuntimeExecutionStepStatus.Cancelled: cancelled++; break;
                case RuntimeExecutionStepStatus.Skipped: skipped++; break;
            }
            progress?.Report(new RuntimeExecutionProgress(
                plan.Count, processed, succeeded, failed, cancelled, skipped));
        }

        (bool stopped, hasSuccess) = await ExecuteSequentialGroupAsync(
            plan.Steps, RuntimeExecutionStepKind.AddEndpointRoute, results, hasSuccess, UpdateProgress, cancellationToken);
        if (stopped)
        {
            FillNullResults(plan.Steps, results, UpdateProgress);
            return BuildFinalResultAndReport(results, hasSuccess, progress);
        }

        (stopped, hasSuccess) = await ExecutePrefixGroupAsync(
            plan.Steps, RuntimeExecutionStepKind.RemovePrefixRoute, results, hasSuccess, DefaultMaxDegreeOfParallelism, progress, cancellationToken);
        if (stopped)
        {
            FillNullResults(plan.Steps, results, UpdateProgress);
            return BuildFinalResultAndReport(results, hasSuccess, progress);
        }

        (stopped, hasSuccess) = await ExecutePrefixGroupAsync(
            plan.Steps, RuntimeExecutionStepKind.AddPrefixRoute, results, hasSuccess, DefaultMaxDegreeOfParallelism, progress, cancellationToken);
        if (stopped)
        {
            FillNullResults(plan.Steps, results, UpdateProgress);
            return BuildFinalResultAndReport(results, hasSuccess, progress);
        }

        (stopped, hasSuccess) = await ExecuteSequentialGroupAsync(
            plan.Steps, RuntimeExecutionStepKind.RemoveEndpointRoute, results, hasSuccess, UpdateProgress, cancellationToken);
        if (stopped)
        {
            FillNullResults(plan.Steps, results, UpdateProgress);
            return BuildFinalResultAndReport(results, hasSuccess, progress);
        }

        return BuildFinalResultAndReport(results, hasSuccess, progress);
    }

    private async Task<(bool Stopped, bool HasSuccess)> ExecuteSequentialGroupAsync(
        IReadOnlyList<RuntimeExecutionStep> allSteps,
        RuntimeExecutionStepKind kind,
        RuntimeExecutionStepResult[] results,
        bool hasSuccess,
        Action<RuntimeExecutionStepStatus> updateProgress,
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
                results[i] = CreateStepResult(allSteps[i], RuntimeExecutionStepStatus.Cancelled);
                updateProgress(RuntimeExecutionStepStatus.Cancelled);
                if (hasSuccess)
                    return (true, hasSuccess);

                throw;
            }

            results[i] = stepResult;
            updateProgress(stepResult.Status);

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

    private async Task<(bool Stopped, bool HasSuccess)> ExecutePrefixGroupAsync(
        IReadOnlyList<RuntimeExecutionStep> allSteps,
        RuntimeExecutionStepKind kind,
        RuntimeExecutionStepResult[] results,
        bool hasSuccess,
        int maxDop,
        IProgress<RuntimeExecutionProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (_prefixGroupHandler is null)
        {
            return await ExecuteBoundedGroupAsync(
                allSteps, kind, results, hasSuccess, maxDop, progress, cancellationToken);
        }

        int[] indices = Enumerable.Range(0, allSteps.Count)
            .Where(i => allSteps[i].Kind == kind)
            .ToArray();

        if (indices.Length == 0)
            return (false, hasSuccess);

        int processed = 0, succeeded = 0, failed = 0, cancelled = 0, skipped = 0;
        object progressLock = new();
        SortedDictionary<int, PrefixMutationResult> attempted = new();
        object attemptLock = new();

        using SemaphoreSlim throttle = new(maxDop, maxDop);
        List<Task> running = new(indices.Length);
        bool groupFailed = false;
        bool anyStarted = false;

        for (int t = 0; t < indices.Length; t++)
        {
            int idx = indices[t];

            if (Volatile.Read(ref groupFailed))
            {
                results[idx] = CreateStepResult(allSteps[idx], RuntimeExecutionStepStatus.Skipped);
                lock (progressLock) { processed++; skipped++; }
                continue;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                if (!anyStarted)
                    cancellationToken.ThrowIfCancellationRequested();

                results[idx] = CreateStepResult(allSteps[idx], RuntimeExecutionStepStatus.Skipped);
                lock (progressLock) { processed++; skipped++; }
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

                results[idx] = CreateStepResult(allSteps[idx], RuntimeExecutionStepStatus.Cancelled);
                lock (progressLock) { processed++; cancelled++; }
                for (int r = t + 1; r < indices.Length; r++)
                {
                    results[indices[r]] = CreateStepResult(allSteps[indices[r]], RuntimeExecutionStepStatus.Skipped);
                    lock (progressLock) { processed++; skipped++; }
                }
                Volatile.Write(ref groupFailed, true);
                break;
            }

            if (Volatile.Read(ref groupFailed))
            {
                throttle.Release();
                results[idx] = CreateStepResult(allSteps[idx], RuntimeExecutionStepStatus.Skipped);
                lock (progressLock) { processed++; skipped++; }
                continue;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                throttle.Release();
                if (!anyStarted)
                    cancellationToken.ThrowIfCancellationRequested();

                results[idx] = CreateStepResult(allSteps[idx], RuntimeExecutionStepStatus.Skipped);
                lock (progressLock) { processed++; skipped++; }
                continue;
            }

            anyStarted = true;
            int captured = idx;
            running.Add(Task.Run(async () =>
            {
                try
                {
                    PrefixMutationResult mutationResult;
                    try
                    {
                        mutationResult =
                            await _prefixGroupHandler.MutatePrefixRouteAsync(
                                allSteps[captured], cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        results[captured] = CreateStepResult(
                            allSteps[captured], RuntimeExecutionStepStatus.Cancelled);
                        lock (progressLock)
                        {
                            processed++; cancelled++;
                            progress?.Report(new RuntimeExecutionProgress(
                                allSteps.Count, processed, succeeded, failed, cancelled, skipped));
                        }
                        Volatile.Write(ref groupFailed, true);
                        return;
                    }

                    lock (attemptLock)
                    {
                        attempted[captured] = mutationResult;
                    }

                    lock (progressLock)
                    {
                        processed++;
                        if (mutationResult.Succeeded) succeeded++; else failed++;
                        progress?.Report(new RuntimeExecutionProgress(
                            allSteps.Count, processed, succeeded, failed, cancelled, skipped));
                    }
                }
                finally
                {
                    throttle.Release();
                }
            }, CancellationToken.None));
        }

        try
        {
            await Task.WhenAll(running);
        }
        catch (OperationCanceledException)
        {
        }

        if (attempted.Count == 0)
            return (Volatile.Read(ref groupFailed), hasSuccess);

        if (groupFailed || cancellationToken.IsCancellationRequested)
        {
            foreach (int planIndex in attempted.Keys)
            {
                results[planIndex] = CreateStepResult(
                    allSteps[planIndex], RuntimeExecutionStepStatus.Cancelled);
            }

            progress?.Report(SummarizeProgress(results));
            return (true, hasSuccess);
        }

        int[] attemptIndices = attempted.Keys.ToArray();
        RuntimeExecutionStep[] attemptSteps = new RuntimeExecutionStep[attemptIndices.Length];
        PrefixMutationResult[] attemptMutations = new PrefixMutationResult[attemptIndices.Length];
        for (int k = 0; k < attemptIndices.Length; k++)
        {
            attemptSteps[k] = allSteps[attemptIndices[k]];
            attemptMutations[k] = attempted[attemptIndices[k]];
        }

        IReadOnlyList<RuntimeExecutionStepResult> verifiedResults;
        try
        {
            verifiedResults = await _prefixGroupHandler.VerifyPrefixRouteGroupAsync(
                attemptSteps, attemptMutations, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            foreach (int planIndex in attemptIndices)
                results[planIndex] = CreateStepResult(
                    allSteps[planIndex], RuntimeExecutionStepStatus.Cancelled);

            progress?.Report(SummarizeProgress(results));
            return (true, hasSuccess);
        }
        catch (Exception)
        {
            foreach (int planIndex in attemptIndices)
                results[planIndex] = CreateStepResult(
                    allSteps[planIndex], RuntimeExecutionStepStatus.Failed);

            progress?.Report(SummarizeProgress(results));
            return (true, hasSuccess);
        }

        if (verifiedResults.Count != attemptIndices.Length)
        {
            foreach (int planIndex in attemptIndices)
                results[planIndex] = CreateStepResult(
                    allSteps[planIndex], RuntimeExecutionStepStatus.Failed);

            progress?.Report(SummarizeProgress(results));
            return (true, hasSuccess);
        }

        bool groupHasSuccess = false;
        for (int k = 0; k < verifiedResults.Count; k++)
        {
            results[attemptIndices[k]] = verifiedResults[k];

            if (verifiedResults[k].Status == RuntimeExecutionStepStatus.Succeeded)
                groupHasSuccess = true;
        }

        bool anyFailed = verifiedResults.Any(
            r => r.Status == RuntimeExecutionStepStatus.Failed);

        progress?.Report(SummarizeProgress(results));

        return (anyFailed, hasSuccess || groupHasSuccess);
    }

    private async Task<(bool Stopped, bool HasSuccess)> ExecuteBoundedGroupAsync(
        IReadOnlyList<RuntimeExecutionStep> allSteps,
        RuntimeExecutionStepKind kind,
        RuntimeExecutionStepResult[] results,
        bool hasSuccess,
        int maxDop,
        IProgress<RuntimeExecutionProgress>? progress,
        CancellationToken cancellationToken)
    {
        int[] indices = Enumerable.Range(0, allSteps.Count)
            .Where(i => allSteps[i].Kind == kind)
            .ToArray();

        if (indices.Length == 0)
            return (false, hasSuccess);

        int processed = 0, succeeded = 0, failed = 0, cancelled = 0, skipped = 0;
        object progressLock = new();

        using SemaphoreSlim throttle = new(maxDop, maxDop);
        List<Task> running = new(indices.Length);
        bool groupFailed = false;
        bool anyStarted = false;

        for (int t = 0; t < indices.Length; t++)
        {
            int idx = indices[t];

            if (Volatile.Read(ref groupFailed))
            {
                results[idx] = CreateStepResult(allSteps[idx], RuntimeExecutionStepStatus.Skipped);
                lock (progressLock) { processed++; skipped++; }
                continue;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                if (!anyStarted)
                    cancellationToken.ThrowIfCancellationRequested();

                results[idx] = CreateStepResult(allSteps[idx], RuntimeExecutionStepStatus.Skipped);
                lock (progressLock) { processed++; skipped++; }
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

                results[idx] = CreateStepResult(allSteps[idx], RuntimeExecutionStepStatus.Cancelled);
                lock (progressLock) { processed++; cancelled++; }
                for (int r = t + 1; r < indices.Length; r++)
                {
                    results[indices[r]] = CreateStepResult(allSteps[indices[r]], RuntimeExecutionStepStatus.Skipped);
                    lock (progressLock) { processed++; skipped++; }
                }
                Volatile.Write(ref groupFailed, true);
                break;
            }

            if (Volatile.Read(ref groupFailed))
            {
                throttle.Release();
                results[idx] = CreateStepResult(allSteps[idx], RuntimeExecutionStepStatus.Skipped);
                lock (progressLock) { processed++; skipped++; }
                continue;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                throttle.Release();
                if (!anyStarted)
                    cancellationToken.ThrowIfCancellationRequested();

                results[idx] = CreateStepResult(allSteps[idx], RuntimeExecutionStepStatus.Skipped);
                lock (progressLock) { processed++; skipped++; }
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
                    r = CreateStepResult(allSteps[captured], RuntimeExecutionStepStatus.Cancelled);
                }

                bool wasFailure = r.Status is RuntimeExecutionStepStatus.Failed or RuntimeExecutionStepStatus.Cancelled;
                if (wasFailure)
                    Volatile.Write(ref groupFailed, true);

                results[captured] = r;
                lock (progressLock)
                {
                    processed++;
                    switch (r.Status)
                    {
                        case RuntimeExecutionStepStatus.Succeeded: succeeded++; break;
                        case RuntimeExecutionStepStatus.Failed: failed++; break;
                        case RuntimeExecutionStepStatus.Cancelled: cancelled++; break;
                        case RuntimeExecutionStepStatus.Skipped: skipped++; break;
                    }
                    progress?.Report(new RuntimeExecutionProgress(
                        allSteps.Count, processed, succeeded, failed, cancelled, skipped));
                }
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
        RuntimeExecutionStepResult[] results,
        Action<RuntimeExecutionStepStatus>? updateProgress = null)
    {
        for (int i = 0; i < results.Length; i++)
        {
            if (results[i] is null)
            {
                results[i] = CreateStepResult(allSteps[i], RuntimeExecutionStepStatus.Skipped);
                updateProgress?.Invoke(RuntimeExecutionStepStatus.Skipped);
            }
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

    private static RuntimeExecutionResult BuildFinalResultAndReport(
        RuntimeExecutionStepResult[] results,
        bool hasSuccess,
        IProgress<RuntimeExecutionProgress>? progress)
    {
        RuntimeExecutionResult finalResult = BuildFinalResult(results, hasSuccess);

        progress?.Report(SummarizeProgress(results));

        return finalResult;
    }

    private static RuntimeExecutionProgress SummarizeProgress(
        RuntimeExecutionStepResult[] results)
    {
        int succeeded = 0, failed = 0, cancelled = 0, skipped = 0;

        foreach (RuntimeExecutionStepResult? r in results)
        {
            switch (r?.Status)
            {
                case RuntimeExecutionStepStatus.Succeeded: succeeded++; break;
                case RuntimeExecutionStepStatus.Failed: failed++; break;
                case RuntimeExecutionStepStatus.Cancelled: cancelled++; break;
                case RuntimeExecutionStepStatus.Skipped: skipped++; break;
            }
        }

        return new RuntimeExecutionProgress(
            results.Length, results.Length, succeeded, failed, cancelled, skipped);
    }

    private static RuntimeExecutionStepResult CreateStepResult(
        RuntimeExecutionStep step, RuntimeExecutionStepStatus status) =>
        new()
        {
            StepIdentity = step.Identity,
            Kind = step.Kind,
            DestinationPrefix = step.DestinationPrefix,
            Status = status
        };
}
