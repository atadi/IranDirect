using System.Text;
using PathVeer.Core.Planning;

namespace PathVeer.Tray;

public static class ExecutionPreviewFormatter
{
    public static string Format(
        ExecutionPreview preview)
    {
        ArgumentNullException.ThrowIfNull(preview);

        var sb = new StringBuilder();

        sb.AppendLine("=== Execution Preview ===");
        sb.AppendLine();

        AppendSummary(sb, preview);

        if (!preview.HasChanges)
        {
            sb.AppendLine();
            sb.AppendLine("No changes would be applied.");
            return sb.ToString();
        }

        sb.AppendLine();
        sb.AppendLine("Steps:");

        foreach (ExecutionPreviewStep step in preview.Steps)
        {
            AppendStep(sb, step);
        }

        return sb.ToString();
    }

    private static void AppendSummary(
        StringBuilder sb,
        ExecutionPreview preview)
    {
        ExecutionPreviewSummary summary = preview.Summary;

        sb.Append("Routes to Create: ");
        sb.AppendLine(summary.CreateCount.ToString());

        sb.Append("Routes to Remove: ");
        sb.AppendLine(summary.DeleteCount.ToString());

        sb.Append("VPN Endpoint Updates: ");
        sb.AppendLine(summary.VpnEndpointUpdates.ToString());

        sb.Append("Inventory Updates: ");
        sb.AppendLine(summary.InventoryUpdates.ToString());

        sb.Append("Estimated Operations: ");
        sb.AppendLine(summary.EstimatedOperations.ToString());

        sb.Append("Has Changes: ");
        sb.AppendLine(
            preview.HasChanges ? "Yes" : "No");

        sb.Append("Captured At: ");
        sb.AppendLine(
            preview.CapturedAt.ToLocalTime()
                .ToString("yyyy-MM-dd HH:mm:ss"));
    }

    private static void AppendStep(
        StringBuilder sb,
        ExecutionPreviewStep step)
    {
        sb.AppendLine();
        sb.Append("  [");
        sb.Append(step.Operation);
        sb.Append("] ");
        sb.AppendLine(step.Category.ToString());

        sb.Append("  Target: ");
        sb.AppendLine(step.Target);

        sb.Append("  Reason: ");
        sb.AppendLine(step.Reason);
    }
}
