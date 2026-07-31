namespace IranDirect.Core.Runtime.Profiling;

using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;

/// <summary>
/// Lightweight ambient cycle profiler. A cycle is opened with
/// <see cref="BeginCycle"/> at the controller level and components
/// anywhere downstream record scoped measurements with
/// <see cref="Measure"/>. When the cycle scope is disposed, a single
/// structured summary is emitted through the provided logger.
///
/// When disabled, or when no cycle is active (e.g. status queries),
/// both methods return a shared no-op scope, so instrumentation
/// overhead is one branch per call site.
/// </summary>
public sealed class RuntimeCycleProfiler
{
    public static RuntimeCycleProfiler Noop { get; } =
        new(enabled: false);

    private static readonly AsyncLocal<CycleContext?> s_current =
        new();

    private readonly bool _enabled;
    private readonly ILogger<RuntimeCycleProfiler>? _logger;

    public RuntimeCycleProfiler(
        bool enabled = true,
        ILogger<RuntimeCycleProfiler>? logger = null)
    {
        _enabled = enabled;
        _logger = logger;
    }

    public bool Enabled => _enabled;

    public IDisposable BeginCycle(string trigger)
    {
        if (!_enabled)
            return NoopScope.Instance;

        CycleContext context = new()
        {
            Trigger = trigger,
            StartTimestamp = Stopwatch.GetTimestamp()
        };

        s_current.Value = context;

        return new CycleScope(this, context);
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
        double totalMs = Stopwatch.GetElapsedTime(
            context.StartTimestamp).TotalMilliseconds;

        IReadOnlyList<RuntimePerfCategorySummary> categories =
            context.Accumulator.Snapshot();

        if (_logger is null)
            return;

        StringBuilder breakdown = new();

        foreach (RuntimePerfCategorySummary summary in categories)
        {
            breakdown.Append(
                $"{summary.Category}" +
                $" n={summary.Count}" +
                $" total={summary.TotalMs:F1}" +
                $" avg={summary.AverageMs:F2}" +
                $" min={summary.MinMs:F1}" +
                $" max={summary.MaxMs:F1}" +
                $" p95={summary.P95Ms:F1}; ");
        }

        _logger.LogInformation(
            "PERF trigger={Trigger} totalMs={TotalMs:F1} " +
            "breakdown={Breakdown}",
            context.Trigger,
            totalMs,
            breakdown.ToString().TrimEnd());
    }

    private sealed class CycleContext
    {
        public required string Trigger { get; init; }

        public required long StartTimestamp { get; init; }

        public RuntimePerfAccumulator Accumulator { get; } = new();
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
