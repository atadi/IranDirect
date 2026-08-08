namespace PathVeer.Core.Runtime.Reconciliation;

public enum RuntimeReconciliationStatus
{
    NoChangesRequired,
    ChangesApplied,
    ChangesPlanned,
    Blocked,
    Failed
}