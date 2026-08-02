using IranDirect.Core.Runtime;
using IranDirect.Core.Runtime.Execution;

namespace IranDirect.Core.Planning;

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

        List<ExecutionPreviewStep> steps = [];

        foreach (RuntimeExecutionStep step in
                 decision.ExecutionPlan.Steps)
        {
            steps.Add(MapStep(step));
        }

        ExecutionPreviewSummary summary = BuildSummary(steps);

        return new ExecutionPreview
        {
            CapturedAt = capturedAt,
            Summary = summary,
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

    private static ExecutionPreviewSummary BuildSummary(
        IReadOnlyList<ExecutionPreviewStep> steps)
    {
        int createCount = 0;
        int deleteCount = 0;
        int vpnEndpointUpdates = 0;
        int inventoryUpdates = 0;

        foreach (ExecutionPreviewStep step in steps)
        {
            switch (step.Operation)
            {
                case ExecutionPreviewOperation.Create:
                    createCount++;
                    break;

                case ExecutionPreviewOperation.Delete:
                    deleteCount++;
                    break;
            }

            if (step.Category ==
                ExecutionPreviewCategory.VpnEndpoint)
            {
                vpnEndpointUpdates++;
            }

            if (step.Category ==
                ExecutionPreviewCategory.Route)
            {
                inventoryUpdates++;
            }
        }

        return new ExecutionPreviewSummary
        {
            CreateCount = createCount,
            DeleteCount = deleteCount,
            VerifyCount = 0,
            InventoryUpdates = inventoryUpdates,
            CustomRouteUpdates = 0,
            VpnEndpointUpdates = vpnEndpointUpdates
        };
    }
}
