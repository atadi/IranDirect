namespace PathVeer.Core.Runtime.Execution;

using PathVeer.Core.Runtime.Reconciliation;

public sealed class RuntimeExecutionPlanner
{
    public RuntimeExecutionPlan Plan(RuntimeChangeSet changeSet)
    {
        ArgumentNullException.ThrowIfNull(changeSet);

        if (changeSet.IsEmpty)
            return new RuntimeExecutionPlan { Steps = [] };

        RuntimeExecutionStep[] steps = changeSet.Changes
            .Select(static change => MapToStep(change))
            .OrderBy(static step => GetPriority(step.Kind))
            .ThenBy(static step => step.Identity, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new RuntimeExecutionPlan { Steps = steps };
    }

    private static int GetPriority(RuntimeExecutionStepKind kind) => kind switch
    {
        RuntimeExecutionStepKind.AddEndpointRoute => 0,
        RuntimeExecutionStepKind.RemovePrefixRoute => 1,
        RuntimeExecutionStepKind.AddPrefixRoute => 2,
        RuntimeExecutionStepKind.RemoveEndpointRoute => 3,
        _ => ThrowArgumentOutOfRange(kind)
    };

    private static RuntimeExecutionStep MapToStep(RuntimeChange change)
    {
        RuntimeExecutionStepKind kind = change.Kind switch
        {
            RuntimeChangeKind.AddEndpointRoute => RuntimeExecutionStepKind.AddEndpointRoute,
            RuntimeChangeKind.RemoveEndpointRoute => RuntimeExecutionStepKind.RemoveEndpointRoute,
            RuntimeChangeKind.AddPrefixRoute => RuntimeExecutionStepKind.AddPrefixRoute,
            RuntimeChangeKind.RemovePrefixRoute => RuntimeExecutionStepKind.RemovePrefixRoute,
            _ => ThrowArgumentOutOfRange(change.Kind)
        };

        return new RuntimeExecutionStep
        {
            Kind = kind,
            Identity = change.Identity,
            DestinationPrefix = change.DestinationPrefix,
            Gateway = change.Gateway,
            InterfaceIndex = change.InterfaceIndex,
            Metric = change.Metric,
            Description = change.Description
        };
    }

    private static int ThrowArgumentOutOfRange(RuntimeExecutionStepKind kind) =>
        throw new ArgumentOutOfRangeException(nameof(kind), kind, null);

    private static RuntimeExecutionStepKind ThrowArgumentOutOfRange(RuntimeChangeKind kind) =>
        throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
}
