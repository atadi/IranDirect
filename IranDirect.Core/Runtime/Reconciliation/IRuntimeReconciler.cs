namespace IranDirect.Core.Runtime.Reconciliation;

public interface IRuntimeReconciler
{
    Task<RuntimeReconciliationResult> ReconcileAsync(
        RuntimePlanSnapshot snapshot,
        CancellationToken cancellationToken = default);
}