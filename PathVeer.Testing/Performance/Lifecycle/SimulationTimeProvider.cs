using System.Threading;

namespace PathVeer.Testing.Performance.Lifecycle;

/// <summary>
/// A deterministic <see cref="TimeProvider"/> used by the lifecycle
/// simulation harness. Time only advances when
/// <see cref="Advance"/> is called, and timers created through
/// <see cref="CreateTimer"/> (for example the scheduled prefix update
/// monitor loop) fire in due order during that advance. No wall-clock
/// time is observed, so accelerated-cycle tests are reproducible.
/// </summary>
public sealed class SimulationTimeProvider : TimeProvider
{
    private readonly object _gate = new();
    private readonly List<SimulationTimer> _timers = [];

    private DateTimeOffset _current;
    private long _ticks;

    public SimulationTimeProvider(
        DateTimeOffset start)
    {
        _current = start;
    }

    public DateTimeOffset Start => _current;

    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate)
        {
            return _current;
        }
    }

    public override long GetTimestamp()
    {
        lock (_gate)
        {
            return _ticks;
        }
    }

    public override long TimestampFrequency => 10_000_000L;

    public override ITimer CreateTimer(
        TimerCallback callback,
        object? state,
        TimeSpan dueTime,
        TimeSpan period)
    {
        ArgumentNullException.ThrowIfNull(callback);

        lock (_gate)
        {
            SimulationTimer timer = new(this, callback, state, period);
            _timers.Add(timer);
            timer.SetDue(dueTime);
            return timer;
        }
    }

    /// <summary>
    /// Advances the simulated clock and fires every timer whose due
    /// time has been reached. Timer callbacks run synchronously in
    /// due order, so continuations (including the scheduled prefix
    /// check loop) run deterministically within this call.
    /// </summary>
    public void Advance(TimeSpan amount)
    {
        if (amount < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(amount),
                "Simulated time cannot move backwards.");
        }

        lock (_gate)
        {
            _current += amount;
            _ticks += amount.Ticks;
            FireDueTimersLocked();
        }
    }

    private void FireDueTimersLocked()
    {
        while (true)
        {
            SimulationTimer? due = null;

            for (int i = _timers.Count - 1; i >= 0; i--)
            {
                SimulationTimer timer = _timers[i];

                if (timer.IsDisposed)
                {
                    _timers.RemoveAt(i);
                    continue;
                }

                if (timer.IsDueAt(_ticks))
                {
                    due = timer;
                    break;
                }
            }

            if (due is null)
            {
                return;
            }

            due.RescheduleAfterFire(_ticks);
            due.InvokeCallback();
        }
    }

    internal long CurrentTicks
    {
        get
        {
            lock (_gate)
            {
                return _ticks;
            }
        }
    }

    private sealed class SimulationTimer : ITimer
    {
        private readonly SimulationTimeProvider _owner;
        private readonly TimerCallback _callback;
        private readonly object? _state;
        private readonly TimeSpan _period;

        private long? _dueTicks;
        private bool _disposed;

        public SimulationTimer(
            SimulationTimeProvider owner,
            TimerCallback callback,
            object? state,
            TimeSpan period)
        {
            _owner = owner;
            _callback = callback;
            _state = state;
            _period = period;
        }

        public bool IsDisposed
        {
            get
            {
                lock (_owner._gate)
                {
                    return _disposed;
                }
            }
        }

        public void SetDue(TimeSpan dueTime)
        {
            lock (_owner._gate)
            {
                _dueTicks = dueTime == Timeout.InfiniteTimeSpan
                    ? null
                    : _owner._ticks + Math.Max(0L, dueTime.Ticks);
            }
        }

        public bool IsDueAt(long nowTicks)
        {
            lock (_owner._gate)
            {
                return _dueTicks is not null
                    && _dueTicks.Value <= nowTicks;
            }
        }

        public void RescheduleAfterFire(long nowTicks)
        {
            lock (_owner._gate)
            {
                if (_period == Timeout.InfiniteTimeSpan)
                {
                    _disposed = true;
                    _dueTicks = null;
                }
                else
                {
                    _dueTicks = nowTicks + Math.Max(1L, _period.Ticks);
                }
            }
        }

        public void InvokeCallback() => _callback(_state);

        public bool Change(
            TimeSpan dueTime,
            TimeSpan period)
        {
            lock (_owner._gate)
            {
                if (_disposed)
                {
                    return false;
                }

                SetDue(dueTime);
                return true;
            }
        }

        public void Dispose()
        {
            lock (_owner._gate)
            {
                _disposed = true;
                _dueTicks = null;
            }
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
