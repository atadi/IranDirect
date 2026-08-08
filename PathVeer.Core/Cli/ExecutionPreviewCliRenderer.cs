using PathVeer.Core.Planning;

namespace PathVeer.Core.Cli;

public static class ExecutionPreviewCliRenderer
{
    public static IEnumerable<string> Render(
        ExecutionPreview preview)
    {
        ArgumentNullException.ThrowIfNull(preview);

        yield return "=== Execution Preview ===";
        yield return string.Empty;
        yield return "Summary";
        yield return string.Empty;

        ExecutionPreviewSummary summary = preview.Summary;

        yield return
            $"Routes to Create: {summary.CreateCount}";
        yield return string.Empty;
        yield return
            $"Routes to Remove: {summary.DeleteCount}";
        yield return string.Empty;
        yield return
            $"VPN Endpoint Updates: " +
            $"{summary.VpnEndpointUpdates}";
        yield return string.Empty;
        yield return
            $"Inventory Updates: {summary.InventoryUpdates}";
        yield return string.Empty;
        yield return
            $"Estimated Operations: " +
            $"{summary.EstimatedOperations}";
        yield return string.Empty;
        yield return
            $"Has Changes: " +
            $"{(summary.HasChanges ? "Yes" : "No")}";

        if (preview.Steps.Count == 0)
        {
            yield return string.Empty;
            yield return
                "No changes would be applied.";
            yield break;
        }

        yield return string.Empty;
        yield return "Execution Plan";
        yield return string.Empty;

        string? lastOperation = null;

        foreach (ExecutionPreviewStep step in preview.Steps)
        {
            string operationLabel =
                $"[{step.Operation}]";

            if (operationLabel != lastOperation)
            {
                if (lastOperation is not null)
                {
                    yield return string.Empty;
                }

                yield return operationLabel;
                lastOperation = operationLabel;
            }

            yield return
                $"  {step.Category}: {step.Target}";
            yield return
                $"  Reason: {step.Reason}";
        }
    }
}
