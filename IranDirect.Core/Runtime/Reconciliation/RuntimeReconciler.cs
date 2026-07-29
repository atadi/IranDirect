namespace IranDirect.Core.Runtime.Reconciliation;

public sealed class RuntimeReconciler : IRuntimeReconciler
{
    private readonly RuntimeRouteOwnershipProvider _ownershipProvider;
    private readonly RuntimeChangeSetPlanner _changeSetPlanner;

    public RuntimeReconciler(
        RuntimeRouteOwnershipProvider ownershipProvider,
        RuntimeChangeSetPlanner changeSetPlanner)
    {
        _ownershipProvider = ownershipProvider;
        _changeSetPlanner = changeSetPlanner;
    }

    public async Task<RuntimeReconciliationResult> ReconcileAsync(
        RuntimePlanSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (!snapshot.Desired.CanReconcile)
        {
            return RuntimeReconciliationResult.Blocked(
                snapshot.Desired.Blockers
                    .Select(b => b.Message));
        }

        RuntimeRouteOwnership ownership;

        try
        {
            ownership =
                await _ownershipProvider.LoadAsync(
                    cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return RuntimeReconciliationResult.Failed(
                [$"Ownership loading failed: {ex.Message}"]);
        }

        RuntimeChangeSet changeSet;

        try
        {
            changeSet = _changeSetPlanner.Plan(
                snapshot, ownership);
        }
        catch (Exception ex)
        {
            return RuntimeReconciliationResult.Failed(
                [$"Change planning failed: {ex.Message}"]);
        }

        if (changeSet.IsEmpty)
        {
            return RuntimeReconciliationResult.NoChanges();
        }

        return RuntimeReconciliationResult.Planned(
            changeSet);
    }
}
