namespace IranDirect.Core.Runtime.Profiling;

using System.Diagnostics;

/// <summary>
/// Lightweight ambient cycle profiler. A cycle is opened with
/// <see cref="BeginCycleIfNone"/> at the controller level and
/// components anywhere downstream record scoped measurements with
/// <see cref="Measure"/>. When the owning scope is disposed, a
/// <see cref="RuntimeCyclePerfReport"/> is written through the
/// configured <see cref="RuntimePerfReportStore"/>.
///
/// Ownership model:
/// - The first caller to begin a cycle for a flow owns the
///   authoritative context. It is the only scope allowed to
///   finalize and persist the report.
/// - Nested <see cref="BeginCycleIfNone"/> calls inside an active
///   cycle return a shared no-op scope that never clears the
///   ambient context and never writes a report. This keeps the
///   outer trigger authoritative when, for example, a repair cycle
///   would otherwise start inside an enable or disable cycle.
///
/// The ambient context is stored in an <see cref="AsyncLocal{T}"/>
/// so it flows through awaits and bounded-parallel child tasks
/// (e.g. <see cref="System.Threading.Tasks.Task.Run"/>) and is
/// cleared only by the owning scope's dispose.
///
/// When disabled, or when no cycle is active (e.g. status queries),
/// begin/measure return a shared no-op scope, so instrumentation
/// overhead is one branch per call site. Report writes are
/// best-effort: a write failure never fails the runtime cycle.
/// </summary>
public sealed class RuntimeCycleProfiler
{
    public static RuntimeCycleProfiler Noop { get; } =
        new(enabled: false);

    private static readonly AsyncLocal<CycleContext?> s_current =
        new();

    private readonly bool _enabled;
    private readonly RuntimePerfReportStore? _store;

    public RuntimeCycleProfiler(
        bool enabled = true,
        RuntimePerfReportStore? store = null)
    {
        _enabled = enabled;
        _store = store;
    }

    public bool Enabled => _enabled;

    /// <summary>
    /// True when a cycle is active in the current async flow.
    /// </summary>
    public bool HasActiveCycle => s_current.Value is not null;

    /// <summary>
    /// Begins a profiling cycle for <paramref name="trigger"/> only
    /// if no cycle is already active in the current flow. When a
    /// cycle is already active, returns a non-owning no-op scope so
    /// the outer trigger remains authoritative.
    /// </summary>
    public IDisposable BeginCycleIfNone(string trigger)
    {
        if (!_enabled)
            return NoopScope.Instance;

        if (s_current.Value is not null)
            return NoopScope.Instance;

        CycleContext context = new(trigger);

        s_current.Value = context;

        return new CycleScope(this, context);
    }

    /// <summary>
    /// Equivalent to <see cref="BeginCycleIfNone"/>; provided for
    /// callers that do not need the "if none" naming.
    /// </summary>
    public IDisposable BeginCycle(string trigger) =>
        BeginCycleIfNone(trigger);

    /// <summary>
    /// Records the final outcome of the active cycle. Called by the
    /// controller before the owning scope is disposed, including on
    /// failure and cancellation paths. Ignored when no cycle is
    /// active. Only the owning flow's outcome is recorded; nested
    /// flows share the same context, so they naturally report the
    /// outer operation's outcome.
    /// </summary>
    public void SetCycleOutcome(
        CycleCompletionStatus status,
        string? errorSummary = null,
        int plannedSteps = 0,
        int completedSteps = 0)
    {
        CycleContext? context = s_current.Value;

        if (context is null)
            return;

        context.CompletionStatus = status;
        context.ErrorSummary = errorSummary;
        context.PlannedSteps = plannedSteps;
        context.CompletedSteps = completedSteps;
    }

    public IDisposable Measure(RuntimePerfCategory category)
    {
        CycleContext? context = s_current.Value;

        if (context is null)
            return NoopScope.Instance;

        return new MeasureScope(
            context.Accumulator,
            category,
            Stopwatch.GetTimestamp());
    }

    private void CompleteCycle(CycleContext context)
    {
        if (_store is null)
            return;

        RuntimeCyclePerfReport report = new()
        {
            Trigger = context.Trigger,
            StartedAt = context.StartedAt,
            CompletedAt = DateTimeOffset.UtcNow,
            TotalMs = Stopwatch.GetElapsedTime(
                context.StartTimestamp).TotalMilliseconds,
            Categories = context.Accumulator.Snapshot(),
            CompletionStatus = context.CompletionStatus,
            ErrorSummary = context.ErrorSummary,
            PlannedSteps = context.PlannedSteps,
            CompletedSteps = context.CompletedSteps
        };

        try
        {
            _store.Write(report);
        }
        catch
        {
            // Instrumentation must never fail the runtime cycle.
        }
    }

    private sealed class CycleContext
    {
        public string Trigger { get; }

        public DateTimeOffset StartedAt { get; }

        public long StartTimestamp { get; }

        public RuntimePerfAccumulator Accumulator { get; } = new();

        public CycleCompletionStatus CompletionStatus { get; set; } =
            CycleCompletionStatus.Completed;

        public string? ErrorSummary { get; set; }

        public int PlannedSteps { get; set; }

        public int CompletedSteps { get; set; }

        public CycleContext(string trigger)
        {
            Trigger = trigger;
            StartedAt = DateTimeOffset.UtcNow;
            StartTimestamp = Stopwatch.GetTimestamp();
        }
    }

    private sealed class CycleScope : IDisposable
    {
        private readonly RuntimeCycleProfiler _profiler;
        private readonly CycleContext _context;
        private bool _disposed;

        public CycleScope(
            RuntimeCycleProfiler profiler,
            CycleContext context)
        {
            _profiler = profiler;
            _context = context;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            s_current.Value = null;
            _profiler.CompleteCycle(_context);
        }
    }

    private sealed class MeasureScope : IDisposable
    {
        private readonly RuntimePerfAccumulator _accumulator;
        private readonly RuntimePerfCategory _category;
        private readonly long _startTimestamp;

        public MeasureScope(
            RuntimePerfAccumulator accumulator,
            RuntimePerfCategory category,
            long startTimestamp)
        {
            _accumulator = accumulator;
            _category = category;
            _startTimestamp = startTimestamp;
        }

        public void Dispose() =>
            _accumulator.Add(
                _category,
                Stopwatch.GetElapsedTime(_startTimestamp));
    }

    private sealed class NoopScope : IDisposable
    {
        public static readonly NoopScope Instance = new();

        public void Dispose()
        {
        }
    }
}
