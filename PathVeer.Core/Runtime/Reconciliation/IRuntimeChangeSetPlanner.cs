namespace PathVeer.Core.Runtime.Reconciliation;

/// <summary>
/// Produces the runtime change set for reconciliation. Implemented by
/// <see cref="RuntimeChangeSetPlanner"/> and any test fake. Kept narrow so the
/// runtime reconciler can be exercised without subclassing the planner.
/// </summary>
public interface IRuntimeChangeSetPlanner
{
    RuntimeChangeSet Plan(
        RuntimePlanSnapshot snapshot,
        RuntimeRouteOwnership ownership);
}
