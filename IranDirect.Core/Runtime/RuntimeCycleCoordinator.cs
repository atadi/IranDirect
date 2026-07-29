using IranDirect.Core.Runtime.Reconciliation;

namespace IranDirect.Core.Runtime;

public sealed class RuntimeCycleCoordinator
{
    private readonly IRuntimePlanCoordinator _planCoordinator;
    private readonly IRuntimeReconciler _reconciler;

    public RuntimeCycleCoordinator(
        IRuntimePlanCoordinator planCoordinator,
        IRuntimeReconciler reconciler)
    {
        _planCoordinator = planCoordinator;
        _reconciler = reconciler;
    }

    public async Task<RuntimeCycleResult> RunCycleAsync(
        CancellationToken cancellationToken = default)
    {
        RuntimePlanSnapshot plan =
            await _planCoordinator.BuildPlanAsync(
                cancellationToken);

        RuntimeReconciliationResult reconciliation =
            await _reconciler.ReconcileAsync(
                plan,
                cancellationToken);

        return new RuntimeCycleResult
        {
            Plan = plan,
            Reconciliation = reconciliation,
            CompletedAt = DateTimeOffset.UtcNow
        };
    }
}
