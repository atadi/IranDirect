using IranDirect.Core.Runtime.Reconciliation;

namespace IranDirect.Core.Runtime;

public sealed record RuntimeCycleResult
{
    public required RuntimePlanSnapshot Plan { get; init; }
    public required RuntimeReconciliationResult Reconciliation { get; init; }
    public DateTimeOffset CompletedAt { get; init; }
}
