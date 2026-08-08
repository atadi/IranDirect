using PathVeer.Core.Runtime;
using PathVeer.Core.Runtime.Execution;

namespace PathVeer.Core.Planning;

public sealed class ExecutionPreviewBuilder : IExecutionPreviewBuilder
{
    private readonly TimeProvider _timeProvider;

    public ExecutionPreviewBuilder(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        _timeProvider = timeProvider;
    }

    public ExecutionPreview Build(RuntimeDecision decision)
    {
        ArgumentNullException.ThrowIfNull(decision);

        DateTimeOffset capturedAt = _timeProvider.GetUtcNow();

        if (decision.ExecutionPlan.IsEmpty)
        {
            return new ExecutionPreview
            {
                CapturedAt = capturedAt,
                Summary = new ExecutionPreviewSummary
                {
                    CreateCount = 0,
                    DeleteCount = 0,
                    VerifyCount = 0,
                    InventoryUpdates = 0,
                    CustomRouteUpdates = 0,
                    VpnEndpointUpdates = 0
                },
                Steps = []
            };
        }

        // Pre-size the step list to the exact source count to avoid the
        // internal growth reallocations a default List<T> would perform.
        List<ExecutionPreviewStep> steps =
            new(decision.ExecutionPlan.Steps.Count);

        int createCount = 0;
        int deleteCount = 0;
        int vpnEndpointUpdates = 0;
        int inventoryUpdates = 0;

        // Single pass: map each source step and accumulate the summary
        // counters inline, eliminating the separate summary enumeration.
        foreach (RuntimeExecutionStep step in
                 decision.ExecutionPlan.Steps)
        {
            ExecutionPreviewStep mapped = MapStep(step);
            steps.Add(mapped);

            switch (mapped.Operation)
            {
                case ExecutionPreviewOperation.Create:
                    createCount++;
                    break;

                case ExecutionPreviewOperation.Delete:
                    deleteCount++;
                    break;
            }

            if (mapped.Category ==
                ExecutionPreviewCategory.VpnEndpoint)
            {
                vpnEndpointUpdates++;
            }
            else if (mapped.Category ==
                     ExecutionPreviewCategory.Route)
            {
                inventoryUpdates++;
            }
        }

        return new ExecutionPreview
        {
            CapturedAt = capturedAt,
            Summary = new ExecutionPreviewSummary
            {
                CreateCount = createCount,
                DeleteCount = deleteCount,
                VerifyCount = 0,
                InventoryUpdates = inventoryUpdates,
                CustomRouteUpdates = 0,
                VpnEndpointUpdates = vpnEndpointUpdates
            },
            Steps = steps
        };
    }

    private static ExecutionPreviewStep MapStep(
        RuntimeExecutionStep step)
    {
        ExecutionPreviewCategory category;
        ExecutionPreviewOperation operation;
        string reason;

        switch (step.Kind)
        {
            case RuntimeExecutionStepKind.AddPrefixRoute:
                category = ExecutionPreviewCategory.Route;
                operation = ExecutionPreviewOperation.Create;
                reason =
                    "Desired prefix route is missing.";
                break;

            case RuntimeExecutionStepKind.RemovePrefixRoute:
                category = ExecutionPreviewCategory.Route;
                operation = ExecutionPreviewOperation.Delete;
                reason =
                    "Owned prefix route is no longer desired.";
                break;

            case RuntimeExecutionStepKind.AddEndpointRoute:
                category =
                    ExecutionPreviewCategory.VpnEndpoint;
                operation =
                    ExecutionPreviewOperation.Create;
                reason =
                    "VPN endpoint protection route is missing.";
                break;

            case RuntimeExecutionStepKind.RemoveEndpointRoute:
                category =
                    ExecutionPreviewCategory.VpnEndpoint;
                operation =
                    ExecutionPreviewOperation.Delete;
                reason =
                    "Owned VPN endpoint route is no longer required.";
                break;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(step.Kind),
                    step.Kind,
                    $"Unsupported execution step kind: {step.Kind}");
        }

        string target =
            string.IsNullOrWhiteSpace(step.DestinationPrefix)
                ? step.Identity
                : step.DestinationPrefix;

        return new ExecutionPreviewStep
        {
            Category = category,
            Operation = operation,
            Target = target,
            Reason = reason
        };
    }
}
