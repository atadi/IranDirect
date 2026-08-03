namespace IranDirect.Testing.Performance.Execution;

public sealed record RuntimeExecutionEvent(string Identity, bool IsStart);

public sealed class RuntimeExecutionConcurrencyProbe
{
    private readonly object _sync = new();

    private readonly List<RuntimeExecutionEvent> _events = [];

    private readonly Dictionary<int, TaskCompletionSource> _activeSignals = [];

    private int _active;

    private int _max;

    public int Active
    {
        get
        {
            lock (_sync)
            {
                return _active;
            }
        }
    }

    public int MaxConcurrency
    {
        get
        {
            lock (_sync)
            {
                return _max;
            }
        }
    }

    public IReadOnlyList<RuntimeExecutionEvent> Events
    {
        get
        {
            lock (_sync)
            {
                return _events.ToArray();
            }
        }
    }

    public void Enter(string identity)
    {
        lock (_sync)
        {
            _events.Add(new RuntimeExecutionEvent(identity, IsStart: true));
            _active++;
            if (_active > _max)
            {
                _max = _active;
            }

            if (_activeSignals.TryGetValue(_active, out TaskCompletionSource? signal))
            {
                signal.TrySetResult();
                _activeSignals.Remove(_active);
            }
        }
    }

    public void Exit(string identity)
    {
        lock (_sync)
        {
            _events.Add(new RuntimeExecutionEvent(identity, IsStart: false));
            _active--;
        }
    }

    public Task WaitForActiveAsync(
        int count,
        int timeoutMilliseconds = 30_000)
    {
        Task task;
        lock (_sync)
        {
            if (_active >= count)
            {
                return Task.CompletedTask;
            }

            var signal = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _activeSignals[count] = signal;

            if (_active >= count)
            {
                signal.TrySetResult();
            }

            task = signal.Task;
        }

        return task.WaitAsync(TimeSpan.FromMilliseconds(timeoutMilliseconds));
    }

    public void Reset()
    {
        lock (_sync)
        {
            _events.Clear();
            _active = 0;
            _max = 0;
            _activeSignals.Clear();
        }
    }
}
