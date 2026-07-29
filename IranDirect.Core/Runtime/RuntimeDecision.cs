namespace IranDirect.Core.Runtime;

using IranDirect.Core.Runtime.Execution;
using IranDirect.Core.Runtime.Reconciliation;

public sealed record RuntimeDecision
{
    private RuntimeDecision() { }

    public required RuntimePlanSnapshot Plan { get; init; }
    public required RuntimeReconciliationResult Reconciliation { get; init; }
    public required RuntimeExecutionPlan ExecutionPlan { get; init; }
    public DateTimeOffset DecidedAt { get; init; }

    public static RuntimeDecision Create(
        RuntimePlanSnapshot plan,
        RuntimeReconciliationResult reconciliation,
        RuntimeExecutionPlan executionPlan,
        DateTimeOffset decidedAt)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(reconciliation);
        ArgumentNullException.ThrowIfNull(executionPlan);

        if (decidedAt == default)
            throw new ArgumentException(
                "DecidedAt must not be the default value.",
                nameof(decidedAt));

        ValidateConsistency(reconciliation, executionPlan);

        return new RuntimeDecision
        {
            Plan = plan,
            Reconciliation = reconciliation,
            ExecutionPlan = executionPlan,
            DecidedAt = decidedAt
        };
    }

    private static void ValidateConsistency(
        RuntimeReconciliationResult reconciliation,
        RuntimeExecutionPlan executionPlan)
    {
        switch (reconciliation.Status)
        {
            case RuntimeReconciliationStatus.NoChangesRequired:
            {
                if (!reconciliation.ChangeSet.IsEmpty)
                    throw new ArgumentException(
                        "NoChangesRequired must have an empty change set.",
                        nameof(reconciliation));

                if (!executionPlan.IsEmpty)
                    throw new ArgumentException(
                        "NoChangesRequired must have an empty execution plan.",
                        nameof(executionPlan));

                return;
            }

            case RuntimeReconciliationStatus.ChangesPlanned:
            {
                if (reconciliation.ChangeSet.IsEmpty)
                    throw new ArgumentException(
                        "ChangesPlanned must have a non-empty change set.",
                        nameof(reconciliation));

                if (executionPlan.IsEmpty)
                    throw new ArgumentException(
                        "ChangesPlanned must have a non-empty execution plan.",
                        nameof(executionPlan));

                break;
            }

            case RuntimeReconciliationStatus.Blocked:
            case RuntimeReconciliationStatus.Failed:
            {
                if (!executionPlan.IsEmpty)
                    throw new ArgumentException(
                        $"{reconciliation.Status} must have an empty execution plan.",
                        nameof(executionPlan));

                return;
            }

            case RuntimeReconciliationStatus.ChangesApplied:
                throw new ArgumentException(
                    "RuntimeDecision is a pre-execution contract. " +
                    "ChangesApplied is not supported.",
                    nameof(reconciliation));

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(reconciliation.Status),
                    reconciliation.Status,
                    null);
        }

        ValidateTraceability(reconciliation.ChangeSet, executionPlan);
    }

    private static void ValidateTraceability(
        RuntimeChangeSet changeSet,
        RuntimeExecutionPlan executionPlan)
    {
        int changeCount = changeSet.Count;
        int stepCount = executionPlan.Count;

        if (changeCount != stepCount)
            throw new ArgumentException(
                $"Change count ({changeCount}) must equal " +
                $"step count ({stepCount}). " +
                "RuntimeExecutionPlanner maps one change to one step.");

        var changePairs = changeSet.Changes
            .Select(change => (
                change.Identity,
                ExpectedKind: MapToExecutionKind(change.Kind)))
            .OrderBy(p => p.Identity, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.ExpectedKind)
            .ToList();

        var stepPairs = executionPlan.Steps
            .Select(step => (step.Identity, step.Kind))
            .OrderBy(p => p.Identity, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.Kind)
            .ToList();

        for (int i = 0; i < changeCount; i++)
        {
            if (changePairs[i] != stepPairs[i])
                throw new ArgumentException(
                    $"Execution step at sorted position {i} does not match " +
                    $"the corresponding change: expected " +
                    $"({changePairs[i].Identity}, {changePairs[i].ExpectedKind}) " +
                    $"but found ({stepPairs[i].Identity}, {stepPairs[i].Kind}).");
        }
    }

    private static RuntimeExecutionStepKind MapToExecutionKind(
        RuntimeChangeKind changeKind) => changeKind switch
    {
        RuntimeChangeKind.AddEndpointRoute =>
            RuntimeExecutionStepKind.AddEndpointRoute,
        RuntimeChangeKind.RemoveEndpointRoute =>
            RuntimeExecutionStepKind.RemoveEndpointRoute,
        RuntimeChangeKind.AddPrefixRoute =>
            RuntimeExecutionStepKind.AddPrefixRoute,
        RuntimeChangeKind.RemovePrefixRoute =>
            RuntimeExecutionStepKind.RemovePrefixRoute,
        _ => throw new ArgumentOutOfRangeException(
            nameof(changeKind), changeKind, null)
    };
}
