using PathVeer.Core.Cli;
using PathVeer.Core.Planning;

namespace PathVeer.Core.Tests.Cli;

public sealed class ExecutionPreviewCliRendererTests
{
    [Fact]
    public void Render_NullPreview_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => ExecutionPreviewCliRenderer
                .Render(null!)
                .ToArray());
    }

    [Fact]
    public void Render_ShowsHeader()
    {
        ExecutionPreview preview = CreatePreview();

        string[] lines = ExecutionPreviewCliRenderer
            .Render(preview)
            .ToArray();

        Assert.Contains(
            "=== Execution Preview ===", lines);
    }

    [Fact]
    public void Render_ShowsSummarySection()
    {
        ExecutionPreview preview = CreatePreview();

        string[] lines = ExecutionPreviewCliRenderer
            .Render(preview)
            .ToArray();

        Assert.Contains("Summary", lines);
    }

    [Fact]
    public void Render_ShowsRoutesToCreate()
    {
        ExecutionPreview preview =
            CreatePreview(createCount: 5);

        string[] lines = ExecutionPreviewCliRenderer
            .Render(preview)
            .ToArray();

        Assert.Contains(
            "Routes to Create: 5", lines);
    }

    [Fact]
    public void Render_ShowsRoutesToRemove()
    {
        ExecutionPreview preview =
            CreatePreview(deleteCount: 3);

        string[] lines = ExecutionPreviewCliRenderer
            .Render(preview)
            .ToArray();

        Assert.Contains(
            "Routes to Remove: 3", lines);
    }

    [Fact]
    public void Render_ShowsVpnEndpointUpdates()
    {
        ExecutionPreview preview =
            CreatePreview(vpnEndpointUpdates: 2);

        string[] lines = ExecutionPreviewCliRenderer
            .Render(preview)
            .ToArray();

        Assert.Contains(
            "VPN Endpoint Updates: 2", lines);
    }

    [Fact]
    public void Render_ShowsInventoryUpdates()
    {
        ExecutionPreview preview =
            CreatePreview(inventoryUpdates: 1946);

        string[] lines = ExecutionPreviewCliRenderer
            .Render(preview)
            .ToArray();

        Assert.Contains(
            "Inventory Updates: 1946", lines);
    }

    [Fact]
    public void Render_ShowsEstimatedOperations()
    {
        ExecutionPreview preview =
            CreatePreview(
                createCount: 1946,
                vpnEndpointUpdates: 1,
                inventoryUpdates: 1946);

        string[] lines = ExecutionPreviewCliRenderer
            .Render(preview)
            .ToArray();

        Assert.Contains(
            "Estimated Operations: 3893", lines);
    }

    [Fact]
    public void Render_ShowsHasChangesYes()
    {
        ExecutionPreview preview =
            CreatePreview(createCount: 1);

        string[] lines = ExecutionPreviewCliRenderer
            .Render(preview)
            .ToArray();

        Assert.Contains("Has Changes: Yes", lines);
    }

    [Fact]
    public void Render_ShowsHasChangesNo()
    {
        ExecutionPreview preview = CreatePreview();

        string[] lines = ExecutionPreviewCliRenderer
            .Render(preview)
            .ToArray();

        Assert.Contains("Has Changes: No", lines);
    }

    [Fact]
    public void Render_EmptyPlan_ShowsNoChangesMessage()
    {
        ExecutionPreview preview = CreatePreview();

        string[] lines = ExecutionPreviewCliRenderer
            .Render(preview)
            .ToArray();

        Assert.Contains(
            "No changes would be applied.", lines);
    }

    [Fact]
    public void Render_EmptyPlan_DoesNotShowExecutionPlan()
    {
        ExecutionPreview preview = CreatePreview();

        string[] lines = ExecutionPreviewCliRenderer
            .Render(preview)
            .ToArray();

        Assert.DoesNotContain("Execution Plan", lines);
    }

    [Fact]
    public void Render_NonEmptyPlan_ShowsExecutionPlan()
    {
        ExecutionPreview preview =
            CreatePreviewWithSteps(
            [
                CreateStep(
                    ExecutionPreviewCategory.Route,
                    ExecutionPreviewOperation.Create,
                    "10.0.0.0/24",
                    "Missing route")
            ]);

        string[] lines = ExecutionPreviewCliRenderer
            .Render(preview)
            .ToArray();

        Assert.Contains("Execution Plan", lines);
    }

    [Fact]
    public void Render_ShowsStepCategory()
    {
        ExecutionPreview preview =
            CreatePreviewWithSteps(
            [
                CreateStep(
                    ExecutionPreviewCategory.VpnEndpoint,
                    ExecutionPreviewOperation.Create,
                    "192.168.1.100",
                    "Missing VPN route")
            ]);

        string[] lines = ExecutionPreviewCliRenderer
            .Render(preview)
            .ToArray();

        Assert.Contains(
            "  VpnEndpoint: 192.168.1.100", lines);
    }

    [Fact]
    public void Render_ShowsStepOperation()
    {
        ExecutionPreview preview =
            CreatePreviewWithSteps(
            [
                CreateStep(
                    ExecutionPreviewCategory.Route,
                    ExecutionPreviewOperation.Delete,
                    "10.0.0.0/24",
                    "Stale route")
            ]);

        string[] lines = ExecutionPreviewCliRenderer
            .Render(preview)
            .ToArray();

        Assert.Contains("[Delete]", lines);
    }

    [Fact]
    public void Render_ShowsStepReason()
    {
        ExecutionPreview preview =
            CreatePreviewWithSteps(
            [
                CreateStep(
                    ExecutionPreviewCategory.Route,
                    ExecutionPreviewOperation.Create,
                    "10.0.0.0/24",
                    "Desired prefix route is missing.")
            ]);

        string[] lines = ExecutionPreviewCliRenderer
            .Render(preview)
            .ToArray();

        Assert.Contains(
            "  Reason: Desired prefix route is missing.",
            lines);
    }

    [Fact]
    public void Render_GroupsByOperation()
    {
        ExecutionPreview preview =
            CreatePreviewWithSteps(
            [
                CreateStep(
                    ExecutionPreviewCategory.Route,
                    ExecutionPreviewOperation.Create,
                    "10.0.0.0/24",
                    "Missing route"),
                CreateStep(
                    ExecutionPreviewCategory.Route,
                    ExecutionPreviewOperation.Create,
                    "10.0.1.0/24",
                    "Missing route"),
                CreateStep(
                    ExecutionPreviewCategory.Route,
                    ExecutionPreviewOperation.Delete,
                    "172.16.0.0/12",
                    "Stale route")
            ]);

        string[] lines = ExecutionPreviewCliRenderer
            .Render(preview)
            .ToArray();

        int firstCreateIndex = Array.FindIndex(
            lines, l => l == "[Create]");
        int deleteIndex = Array.FindIndex(
            lines, l => l == "[Delete]");

        Assert.True(firstCreateIndex >= 0);
        Assert.True(deleteIndex > firstCreateIndex);
    }

    [Fact]
    public void Render_OperationHeaderShownOnce()
    {
        ExecutionPreview preview =
            CreatePreviewWithSteps(
            [
                CreateStep(
                    ExecutionPreviewCategory.Route,
                    ExecutionPreviewOperation.Create,
                    "10.0.0.0/24",
                    "Missing route"),
                CreateStep(
                    ExecutionPreviewCategory.VpnEndpoint,
                    ExecutionPreviewOperation.Create,
                    "192.168.1.100",
                    "Missing VPN route")
            ]);

        string[] lines = ExecutionPreviewCliRenderer
            .Render(preview)
            .ToArray();

        int createCount = lines.Count(
            l => l == "[Create]");

        Assert.Equal(1, createCount);
    }

    [Fact]
    public void Render_EmptyPlan_NoOperationHeaders()
    {
        ExecutionPreview preview = CreatePreview();

        string[] lines = ExecutionPreviewCliRenderer
            .Render(preview)
            .ToArray();

        Assert.DoesNotContain("[Create]", lines);
        Assert.DoesNotContain("[Delete]", lines);
        Assert.DoesNotContain("[Verify]", lines);
    }

    private static ExecutionPreview CreatePreview(
        int createCount = 0,
        int deleteCount = 0,
        int vpnEndpointUpdates = 0,
        int inventoryUpdates = 0)
    {
        return new ExecutionPreview
        {
            CapturedAt = DateTimeOffset.UtcNow,
            Summary = new ExecutionPreviewSummary
            {
                CreateCount = createCount,
                DeleteCount = deleteCount,
                VerifyCount = 0,
                InventoryUpdates = inventoryUpdates,
                CustomRouteUpdates = 0,
                VpnEndpointUpdates = vpnEndpointUpdates
            },
            Steps = []
        };
    }

    private static ExecutionPreview CreatePreviewWithSteps(
        ExecutionPreviewStep[] steps)
    {
        int createCount =
            steps.Count(s =>
                s.Operation ==
                ExecutionPreviewOperation.Create);
        int deleteCount =
            steps.Count(s =>
                s.Operation ==
                ExecutionPreviewOperation.Delete);
        int vpnEndpointUpdates =
            steps.Count(s =>
                s.Category ==
                ExecutionPreviewCategory.VpnEndpoint);
        int inventoryUpdates =
            steps.Count(s =>
                s.Category ==
                ExecutionPreviewCategory.Route);

        return new ExecutionPreview
        {
            CapturedAt = DateTimeOffset.UtcNow,
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

    private static ExecutionPreviewStep CreateStep(
        ExecutionPreviewCategory category,
        ExecutionPreviewOperation operation,
        string target,
        string reason)
    {
        return new ExecutionPreviewStep
        {
            Category = category,
            Operation = operation,
            Target = target,
            Reason = reason
        };
    }
}
