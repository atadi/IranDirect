namespace PathVeer.Core.Runtime;

using PathVeer.Core.Runtime.Profiling;

public sealed class RuntimeCycleCoordinator
{
    private readonly IRuntimeDecisionBuilder _decisionBuilder;
    private readonly RuntimeCycleProfiler _profiler;

    public RuntimeCycleCoordinator(
        IRuntimeDecisionBuilder decisionBuilder,
        RuntimeCycleProfiler? profiler = null)
    {
        ArgumentNullException.ThrowIfNull(decisionBuilder);

        _decisionBuilder = decisionBuilder;
        _profiler = profiler ?? RuntimeCycleProfiler.Noop;
    }

    public async Task<RuntimeDecision> RunCycleAsync(
        CancellationToken cancellationToken = default)
    {
        using (_profiler.Measure(
            RuntimePerfCategory.PlanningDecisionBuild))
        {
            return await _decisionBuilder.BuildAsync(
                cancellationToken);
        }
    }
}
