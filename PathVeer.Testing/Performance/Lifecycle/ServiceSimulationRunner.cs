using PathVeer.Core.Runtime.Execution;

namespace PathVeer.Testing.Performance.Lifecycle;

/// <summary>
/// Executes a validated <see cref="ServiceSimulationPlan"/> against a
/// <see cref="SimulatedRuntimeEnvironment"/>, collecting metrics,
/// resource samples, and trend lines.
/// </summary>
public static class ServiceSimulationRunner
{
    public static async Task<ServiceSimulationRunResult> RunAsync(
        SimulatedRuntimeEnvironment environment,
        ServiceSimulationPlan plan,
        ServiceSimulationRunOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(plan);

        ServiceSimulationRunOptions runOptions =
            options ?? new ServiceSimulationRunOptions();

        ServiceSimulationMetrics metrics = new();
        ServiceSimulationSampler sampler = new(environment, runOptions);
        ServiceSimulationContext context = new(
            environment,
            metrics,
            sampler);

        await sampler.CaptureBaselineAsync(cancellationToken);

        RuntimeExecutionResult? lastCycleResult = null;

        foreach (IServiceSimulationStep step in plan.Steps)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await step.ExecuteAsync(context, cancellationToken);

            lastCycleResult = context.LastCycleResult;
        }

        if (runOptions.CaptureFinalSample)
        {
            await sampler.CaptureFinalAsync(cancellationToken);
        }

        IReadOnlyList<ResourceSample> samples = sampler.Samples;

        return new ServiceSimulationRunResult
        {
            PlanName = plan.Name,
            Metrics = metrics,
            Samples = samples,
            Trends = ResourceTrendAnalyzer.Analyze(samples),
            LastCycleResult = lastCycleResult
        };
    }
}
