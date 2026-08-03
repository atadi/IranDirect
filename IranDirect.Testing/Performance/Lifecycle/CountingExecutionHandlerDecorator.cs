using IranDirect.Core.Runtime.Execution;

namespace IranDirect.Testing.Performance.Lifecycle;

/// <summary>
/// Counting decorator over an <see cref="IRuntimeExecutionStepHandler"/>.
/// Records the peak and current number of in-flight handler calls so
/// the harness can assert that no execution work remains active after
/// a cycle (the "no active execution-handler calls" invariant).
/// </summary>
public sealed class CountingExecutionHandlerDecorator :
    IRuntimeExecutionStepHandler,
    IPrefixGroupExecutionHandler
{
    private readonly IRuntimeExecutionStepHandler _inner;
    private readonly IPrefixGroupExecutionHandler _prefixGroupInner;
    private readonly Func<Task>? _hold;
    private readonly object _gate = new();

    private int _active;
    private int _peak;
    private long _totalCalls;
    private long _mutateCalls;
    private long _verifyCalls;

    public CountingExecutionHandlerDecorator(
        IRuntimeExecutionStepHandler inner,
        Func<Task>? hold = null)
    {
        _inner = inner;
        _prefixGroupInner = inner as IPrefixGroupExecutionHandler
            ?? throw new ArgumentException(
                "The inner handler must implement " +
                nameof(IPrefixGroupExecutionHandler),
                nameof(inner));
        _hold = hold;
    }

    public int ActiveCalls
    {
        get
        {
            lock (_gate)
            {
                return _active;
            }
        }
    }

    public int PeakActiveCalls
    {
        get
        {
            lock (_gate)
            {
                return _peak;
            }
        }
    }

    public long TotalCalls
    {
        get
        {
            lock (_gate)
            {
                return _totalCalls;
            }
        }
    }

    public long MutateCalls
    {
        get
        {
            lock (_gate)
            {
                return _mutateCalls;
            }
        }
    }

    public long VerifyCalls
    {
        get
        {
            lock (_gate)
            {
                return _verifyCalls;
            }
        }
    }

    public async Task<RuntimeExecutionStepResult> ExecuteAndVerifyAsync(
        RuntimeExecutionStep step,
        CancellationToken cancellationToken = default)
    {
        if (_hold is not null)
        {
            await _hold();
        }

        using (Enter())
        {
            return await _inner.ExecuteAndVerifyAsync(
                step, cancellationToken);
        }
    }

    public async Task<PrefixMutationResult> MutatePrefixRouteAsync(
        RuntimeExecutionStep step,
        CancellationToken cancellationToken = default)
    {
        if (_hold is not null)
        {
            await _hold();
        }

        using (Enter())
        {
            Interlocked.Increment(ref _mutateCalls);
            return await _prefixGroupInner.MutatePrefixRouteAsync(
                step, cancellationToken);
        }
    }

    public async Task<IReadOnlyList<RuntimeExecutionStepResult>>
        VerifyPrefixRouteGroupAsync(
            IReadOnlyList<RuntimeExecutionStep> steps,
            IReadOnlyList<PrefixMutationResult> mutationResults,
            CancellationToken cancellationToken = default)
    {
        if (_hold is not null)
        {
            await _hold();
        }

        using (Enter())
        {
            Interlocked.Increment(ref _verifyCalls);
            return await _prefixGroupInner.VerifyPrefixRouteGroupAsync(
                steps, mutationResults, cancellationToken);
        }
    }

    private IDisposable Enter()
    {
        lock (_gate)
        {
            _active++;
            _totalCalls++;
            if (_active > _peak)
            {
                _peak = _active;
            }
        }

        return new ExitScope(this);
    }

    private sealed class ExitScope : IDisposable
    {
        private readonly CountingExecutionHandlerDecorator _owner;

        public ExitScope(CountingExecutionHandlerDecorator owner)
        {
            _owner = owner;
        }

        public void Dispose()
        {
            lock (_owner._gate)
            {
                _owner._active--;
            }
        }
    }
}
