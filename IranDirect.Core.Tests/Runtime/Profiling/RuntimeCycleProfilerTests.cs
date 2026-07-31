namespace IranDirect.Core.Tests.Runtime.Profiling;

using IranDirect.Core.Runtime.Profiling;
using Microsoft.Extensions.Logging;

public sealed class RuntimeCycleProfilerTests
{
    [Fact]
    public void Measure_WithoutActiveCycle_DoesNotThrow()
    {
        RuntimeCycleProfiler profiler = new(enabled: true);

        using IDisposable scope = profiler.Measure(
            RuntimePerfCategory.ExecutionRouteCreate);

        Assert.NotNull(scope);
    }

    [Fact]
    public void BeginCycle_WhenDisabled_EmitsNoLog()
    {
        FakeLogger logger = new();
        RuntimeCycleProfiler profiler = new(
            enabled: false, logger);

        using (profiler.BeginCycle("enable"))
        {
            using IDisposable scope = profiler.Measure(
                RuntimePerfCategory.ExecutionRouteCreate);
        }

        Assert.Empty(logger.Messages);
    }

    [Fact]
    public void BeginCycle_WhenEnabled_EmitsSingleSummaryLog()
    {
        FakeLogger logger = new();
        RuntimeCycleProfiler profiler = new(
            enabled: true, logger);

        using (profiler.BeginCycle("enable"))
        {
            using IDisposable scope = profiler.Measure(
                RuntimePerfCategory.ObservationPrefixLoad);
        }

        string message = Assert.Single(logger.Messages);
        Assert.Contains("PERF", message);
        Assert.Contains("trigger=enable", message);
    }

    [Fact]
    public void Measure_WithinCycle_AppearsInSummaryBreakdown()
    {
        FakeLogger logger = new();
        RuntimeCycleProfiler profiler = new(
            enabled: true, logger);

        using (profiler.BeginCycle("repair"))
        {
            using (profiler.Measure(
                RuntimePerfCategory.ExecutionRouteVerify))
            {
            }

            using (profiler.Measure(
                RuntimePerfCategory.PersistenceStateSave))
            {
            }
        }

        string message = Assert.Single(logger.Messages);
        Assert.Contains(
            nameof(RuntimePerfCategory.ExecutionRouteVerify),
            message);
        Assert.Contains(
            nameof(RuntimePerfCategory.PersistenceStateSave),
            message);
        Assert.Contains("trigger=repair", message);
    }

    [Fact]
    public void Measure_AfterCycleDisposed_IsNotRecorded()
    {
        FakeLogger logger = new();
        RuntimeCycleProfiler profiler = new(
            enabled: true, logger);

        using (profiler.BeginCycle("enable"))
        {
        }

        using IDisposable scope = profiler.Measure(
            RuntimePerfCategory.ExecutionRouteDelete);

        string message = Assert.Single(logger.Messages);
        Assert.DoesNotContain(
            nameof(RuntimePerfCategory.ExecutionRouteDelete),
            message);
    }

    [Fact]
    public void CycleScope_DisposedTwice_EmitsOnce()
    {
        FakeLogger logger = new();
        RuntimeCycleProfiler profiler = new(
            enabled: true, logger);

        IDisposable cycle = profiler.BeginCycle("disable");
        cycle.Dispose();
        cycle.Dispose();

        Assert.Single(logger.Messages);
    }

    [Fact]
    public void Noop_BeginCycle_EmitsNoLog()
    {
        using (RuntimeCycleProfiler.Noop.BeginCycle("enable"))
        {
            using IDisposable scope =
                RuntimeCycleProfiler.Noop.Measure(
                    RuntimePerfCategory.ExecutionRouteCreate);
        }

        Assert.False(RuntimeCycleProfiler.Noop.Enabled);
    }

    private sealed class FakeLogger : ILogger<RuntimeCycleProfiler>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }
    }
}
