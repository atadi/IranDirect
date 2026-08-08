using PathVeer.Core.Runtime.Execution;

namespace PathVeer.Testing.Performance.Execution;

public sealed class ControlledRuntimeExecutionHandler :
    IRuntimeExecutionStepHandler,
    IPrefixGroupExecutionHandler
{
    private readonly RuntimeExecutionConcurrencyProbe? _probe;

    private readonly Func<RuntimeExecutionStep, CancellationToken, Task>? _gate;

    private readonly object _sync = new();

    private readonly List<string> _verified = [];

    private int _mutationCallCount;

    private int _stepCallCount;

    private int _verifyCallCount;

    public ControlledRuntimeExecutionHandler(
        RuntimeExecutionConcurrencyProbe? probe = null,
        Func<RuntimeExecutionStep, CancellationToken, Task>? gate = null)
    {
        _probe = probe;
        _gate = gate;
    }

    public IReadOnlySet<string> FailIdentities { get; set; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlySet<string> CancelIdentities { get; set; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlySet<string> BenignRaceIdentities { get; set; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public int MutationCallCount
    {
        get
        {
            lock (_sync)
            {
                return _mutationCallCount;
            }
        }
    }

    public int StepCallCount
    {
        get
        {
            lock (_sync)
            {
                return _stepCallCount;
            }
        }
    }

    public int VerifyCallCount
    {
        get
        {
            lock (_sync)
            {
                return _verifyCallCount;
            }
        }
    }

    public IReadOnlyList<string> VerifiedIdentities
    {
        get
        {
            lock (_sync)
            {
                return _verified.ToArray();
            }
        }
    }

    public async Task<RuntimeExecutionStepResult> ExecuteAndVerifyAsync(
        RuntimeExecutionStep step,
        CancellationToken cancellationToken = default)
    {
        _probe?.Enter(step.Identity);
        try
        {
            if (_gate is not null)
            {
                await _gate(step, cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();

            lock (_sync)
            {
                _stepCallCount++;
            }

            if (CancelIdentities.Contains(step.Identity))
            {
                throw new OperationCanceledException(cancellationToken);
            }

            RuntimeExecutionStepStatus status = FailIdentities.Contains(step.Identity)
                ? RuntimeExecutionStepStatus.Failed
                : RuntimeExecutionStepStatus.Succeeded;

            return new RuntimeExecutionStepResult
            {
                StepIdentity = step.Identity,
                Kind = step.Kind,
                DestinationPrefix = step.DestinationPrefix,
                Status = status,
                ErrorMessage = status == RuntimeExecutionStepStatus.Failed
                    ? $"Step '{step.Identity}' failed."
                    : null
            };
        }
        finally
        {
            _probe?.Exit(step.Identity);
        }
    }

    public async Task<PrefixMutationResult> MutatePrefixRouteAsync(
        RuntimeExecutionStep step,
        CancellationToken cancellationToken = default)
    {
        _probe?.Enter(step.Identity);
        try
        {
            if (_gate is not null)
            {
                await _gate(step, cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();

            lock (_sync)
            {
                _mutationCallCount++;
            }

            if (CancelIdentities.Contains(step.Identity))
            {
                throw new OperationCanceledException(cancellationToken);
            }

            if (FailIdentities.Contains(step.Identity) ||
                BenignRaceIdentities.Contains(step.Identity))
            {
                return PrefixMutationResult.Failure(
                    $"Mutation for '{step.Identity}' reported a benign race.");
            }

            return PrefixMutationResult.Success();
        }
        finally
        {
            _probe?.Exit(step.Identity);
        }
    }

    public Task<IReadOnlyList<RuntimeExecutionStepResult>> VerifyPrefixRouteGroupAsync(
        IReadOnlyList<RuntimeExecutionStep> steps,
        IReadOnlyList<PrefixMutationResult> mutationResults,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var results = new RuntimeExecutionStepResult[steps.Count];

        lock (_sync)
        {
            _verifyCallCount++;
        }

        for (int i = 0; i < steps.Count; i++)
        {
            RuntimeExecutionStep step = steps[i];
            bool failed = FailIdentities.Contains(step.Identity);

            results[i] = new RuntimeExecutionStepResult
            {
                StepIdentity = step.Identity,
                Kind = step.Kind,
                DestinationPrefix = step.DestinationPrefix,
                Status = failed
                    ? RuntimeExecutionStepStatus.Failed
                    : RuntimeExecutionStepStatus.Succeeded,
                ErrorMessage = failed
                    ? $"Group verification failed for '{step.Identity}'."
                    : null
            };
        }

        lock (_sync)
        {
            _verified.AddRange(steps.Select(static step => step.Identity));
        }

        return Task.FromResult<IReadOnlyList<RuntimeExecutionStepResult>>(results);
    }
}
