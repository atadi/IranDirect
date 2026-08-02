using IranDirect.Core.Planning;

namespace IranDirect.Tray;

public sealed record ExecutionPreviewSummaryRow(
    string Label,
    string Value);

public sealed record ExecutionPreviewStepRow(
    string Operation,
    string Category,
    string Target,
    string Reason);

public sealed record ExecutionPreviewStepExplanation(
    string? Title,
    string? Summary,
    string? Details);

public sealed record ExecutionPreviewStepDetail(
    ExecutionPreviewStepRow Row,
    ExecutionPreviewStepExplanation? Explanation);

public sealed record ExecutionPreviewDisplay(
    bool HasChanges,
    IReadOnlyList<ExecutionPreviewSummaryRow> Summary,
    IReadOnlyList<ExecutionPreviewStepDetail> Steps);

public static class ExecutionPreviewDialogModel
{
    public static ExecutionPreviewDisplay Map(
        ExecutionPreview preview)
    {
        ArgumentNullException.ThrowIfNull(preview);

        IReadOnlyList<ExecutionPreviewSummaryRow> summary =
            MapSummary(preview);

        IReadOnlyList<ExecutionPreviewStepDetail> steps =
            MapSteps(preview);

        return new ExecutionPreviewDisplay(
            preview.HasChanges,
            summary,
            steps);
    }

    public static string GetCopyText(
        ExecutionPreview preview)
    {
        return ExecutionPreviewFormatter.Format(preview);
    }

    private static IReadOnlyList<ExecutionPreviewSummaryRow>
        MapSummary(ExecutionPreview preview)
    {
        ExecutionPreviewSummary summary = preview.Summary;

        return
        [
            new ExecutionPreviewSummaryRow(
                "Routes to Create",
                summary.CreateCount.ToString()),
            new ExecutionPreviewSummaryRow(
                "Routes to Remove",
                summary.DeleteCount.ToString()),
            new ExecutionPreviewSummaryRow(
                "VPN Endpoint Updates",
                summary.VpnEndpointUpdates.ToString()),
            new ExecutionPreviewSummaryRow(
                "Inventory Updates",
                summary.InventoryUpdates.ToString()),
            new ExecutionPreviewSummaryRow(
                "Estimated Operations",
                summary.EstimatedOperations.ToString()),
            new ExecutionPreviewSummaryRow(
                "Has Changes",
                preview.HasChanges ? "Yes" : "No"),
            new ExecutionPreviewSummaryRow(
                "Captured At",
                preview.CapturedAt.ToLocalTime()
                    .ToString("yyyy-MM-dd HH:mm:ss"))
        ];
    }

    private static IReadOnlyList<ExecutionPreviewStepDetail>
        MapSteps(ExecutionPreview preview)
    {
        List<ExecutionPreviewStepDetail> steps = [];

        foreach (ExecutionPreviewStep step in preview.Steps)
        {
            ExecutionPreviewStepRow row = new(
                step.Operation.ToString(),
                step.Category.ToString(),
                step.Target,
                step.Reason);

            steps.Add(new ExecutionPreviewStepDetail(
                row, null));
        }

        return steps;
    }
}
