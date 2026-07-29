namespace IranDirect.Core.Runtime;

using IranDirect.Core.Runtime.Execution;
using IranDirect.Core.Runtime.Reconciliation;

public sealed class RuntimeDecisionBuilder : IRuntimeDecisionBuilder
{
    private readonly IRuntimePlanCoordinator _planCoordinator;
    private readonly IRuntimeReconciler _reconciler;
    private readonly RuntimeExecutionPlanner _executionPlanner;
    private readonly TimeProvider _timeProvider;

    public RuntimeDecisionBuilder(
        IRuntimePlanCoordinator planCoordinator,
        IRuntimeReconciler reconciler,
        RuntimeExecutionPlanner executionPlanner,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(planCoordinator);
        ArgumentNullException.ThrowIfNull(reconciler);
        ArgumentNullException.ThrowIfNull(executionPlanner);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _planCoordinator = planCoordinator;
        _reconciler = reconciler;
        _executionPlanner = executionPlanner;
        _timeProvider = timeProvider;
    }

    public async Task<RuntimeDecision> BuildAsync(
        CancellationToken cancellationToken = default)
    {
        RuntimePlanSnapshot plan =
            await _planCoordinator.BuildPlanAsync(
                cancellationToken);

        RuntimeReconciliationResult reconciliation =
            await _reconciler.ReconcileAsync(
                plan,
                cancellationToken);

        RuntimeExecutionPlan executionPlan =
            _executionPlanner.Plan(
                reconciliation.ChangeSet);

        DateTimeOffset decidedAt =
            _timeProvider.GetUtcNow();

        return RuntimeDecision.Create(
            plan,
            reconciliation,
            executionPlan,
            decidedAt);
    }
}
