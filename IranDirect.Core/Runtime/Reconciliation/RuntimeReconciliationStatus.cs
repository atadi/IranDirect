namespace IranDirect.Core.Runtime.Reconciliation;

public enum RuntimeReconciliationStatus
{
    NoChangesRequired,
    ChangesApplied,
    ChangesPlanned,
    Blocked,
    Failed
}