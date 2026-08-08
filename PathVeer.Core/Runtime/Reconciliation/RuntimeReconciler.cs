namespace PathVeer.Core.Runtime.Reconciliation;

using PathVeer.Core.Observability.Telemetry;

public sealed class RuntimeReconciler : IRuntimeReconciler
{
    private readonly RuntimeRouteOwnershipProvider _ownershipProvider;
    private readonly IRuntimeChangeSetPlanner _changeSetPlanner;

    public RuntimeReconciler(
        RuntimeRouteOwnershipProvider ownershipProvider,
        IRuntimeChangeSetPlanner changeSetPlanner)
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

        using (RuntimePlanningTelemetryScope planning =
            RuntimePlanningTelemetry.Start())
        {
            try
            {
                changeSet = _changeSetPlanner.Plan(
                    snapshot, ownership);
                planning.CompleteSuccess(changeSet.Count);
                RuntimePlanningTelemetry.RecordChangedRoutes(
                    changeSet.Count > 0
                        ? IranDirectTagValues.Success
                        : IranDirectTagValues.NoChange,
                    changeSet.Count);
            }
            catch (Exception ex)
            {
                planning.CompleteFailure(ex);
                return RuntimeReconciliationResult.Failed(
                    [$"Change planning failed: {ex.Message}"]);
            }
        }

        if (changeSet.IsEmpty)
        {
            return RuntimeReconciliationResult.NoChanges();
        }

        return RuntimeReconciliationResult.Planned(
            changeSet);
    }
}
