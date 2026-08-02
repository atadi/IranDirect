using IranDirect.Core.Planning;
using IranDirect.Tray;

namespace IranDirect.Core.Tests.Tray;

public sealed class ExecutionPreviewDialogModelTests
{
    private static ExecutionPreview BuildPreview(
        params ExecutionPreviewStep[] steps)
    {
        return BuildPreviewInternal(
            capturedAt: null,
            steps: steps);
    }

    private static ExecutionPreview BuildPreview(
        DateTimeOffset capturedAt,
        params ExecutionPreviewStep[] steps)
    {
        return BuildPreviewInternal(
            capturedAt: capturedAt,
            steps: steps);
    }

    private static ExecutionPreview BuildPreviewInternal(
        DateTimeOffset? capturedAt,
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

        return new ExecutionPreview
        {
            CapturedAt = capturedAt
                ?? new DateTimeOffset(
                    2026, 8, 3, 12, 0, 0, TimeSpan.Zero),
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

    [Fact]
    public void Map_NullPreview_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => ExecutionPreviewDialogModel.Map(null!));
    }

    [Fact]
    public void Map_SummaryContainsAllRequiredFields()
    {
        ExecutionPreview preview = BuildPreview(
            CreateStep(
                ExecutionPreviewOperation.Create,
                ExecutionPreviewCategory.Route,
                "10.0.0.0/24",
                "missing"));

        ExecutionPreviewDisplay display =
            ExecutionPreviewDialogModel.Map(preview);

        string[] labels = display.Summary
            .Select(s => s.Label)
            .ToArray();

        Assert.Contains("Routes to Create", labels);
        Assert.Contains("Routes to Remove", labels);
        Assert.Contains("VPN Endpoint Updates", labels);
        Assert.Contains("Inventory Updates", labels);
        Assert.Contains("Estimated Operations", labels);
        Assert.Contains("Has Changes", labels);
        Assert.Contains("Captured At", labels);
    }

    [Fact]
    public void Map_SummaryCreateDeleteAndEndpointCounts()
    {
        ExecutionPreview preview = BuildPreview(
            CreateStep(
                ExecutionPreviewOperation.Create,
                ExecutionPreviewCategory.Route,
                "10.0.0.0/24"),
            CreateStep(
                ExecutionPreviewOperation.Create,
                ExecutionPreviewCategory.Route,
                "10.0.1.0/24"),
            CreateStep(
                ExecutionPreviewOperation.Delete,
                ExecutionPreviewCategory.Route,
                "10.0.2.0/24"),
            CreateStep(
                ExecutionPreviewOperation.Create,
                ExecutionPreviewCategory.VpnEndpoint,
                "192.168.100.1"));

        ExecutionPreviewDisplay display =
            ExecutionPreviewDialogModel.Map(preview);

        string Create =
            display.Summary
                .First(s => s.Label == "Routes to Create").Value;
        string Delete =
            display.Summary
                .First(s => s.Label == "Routes to Remove").Value;
        string Vpn =
            display.Summary
                .First(s => s.Label == "VPN Endpoint Updates").Value;
        string Inventory =
            display.Summary
                .First(s => s.Label == "Inventory Updates").Value;
        string Estimated =
            display.Summary
                .First(s => s.Label == "Estimated Operations").Value;
        string HasChanges =
            display.Summary
                .First(s => s.Label == "Has Changes").Value;

        Assert.Equal("3", Create);
        Assert.Equal("1", Delete);
        Assert.Equal("1", Vpn);
        Assert.Equal("3", Inventory);
        Assert.Equal("8", Estimated);
        Assert.Equal("Yes", HasChanges);
    }

    [Fact]
    public void Map_CapturedAtFormattedAsLocalTime()
    {
        DateTimeOffset utc = new(
            2026, 8, 3, 10, 30, 0, TimeSpan.Zero);

        ExecutionPreview preview = BuildPreview(
            capturedAt: utc);

        ExecutionPreviewDisplay display =
            ExecutionPreviewDialogModel.Map(preview);

        string captured =
            display.Summary
                .First(s => s.Label == "Captured At").Value;

        string expected =
            utc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

        Assert.Equal(expected, captured);
    }

    [Fact]
    public void Map_PreservesAuthoritativeStepOrdering()
    {
        ExecutionPreviewStep first =
            CreateStep(
                ExecutionPreviewOperation.Create,
                ExecutionPreviewCategory.Route,
                "10.0.0.0/24",
                "first");
        ExecutionPreviewStep second =
            CreateStep(
                ExecutionPreviewOperation.Delete,
                ExecutionPreviewCategory.Route,
                "10.0.1.0/24",
                "second");
        ExecutionPreviewStep third =
            CreateStep(
                ExecutionPreviewOperation.Create,
                ExecutionPreviewCategory.VpnEndpoint,
                "192.168.100.1",
                "third");

        ExecutionPreviewDisplay display =
            ExecutionPreviewDialogModel.Map(
                BuildPreview(first, second, third));

        Assert.Equal(3, display.Steps.Count);
        Assert.Equal("10.0.0.0/24", display.Steps[0].Row.Target);
        Assert.Equal("first", display.Steps[0].Row.Reason);
        Assert.Equal("10.0.1.0/24", display.Steps[1].Row.Target);
        Assert.Equal("second", display.Steps[1].Row.Reason);
        Assert.Equal("192.168.100.1", display.Steps[2].Row.Target);
        Assert.Equal("third", display.Steps[2].Row.Reason);
    }

    [Fact]
    public void Map_EmptyPreview_HasNoStepsAndHasChangesIsFalse()
    {
        ExecutionPreviewDisplay display =
            ExecutionPreviewDialogModel.Map(
                BuildPreview());

        Assert.False(display.HasChanges);
        Assert.Empty(display.Steps);
    }

    [Fact]
    public void Map_EmptyPreview_SummaryHasChangesIsNo()
    {
        ExecutionPreviewDisplay display =
            ExecutionPreviewDialogModel.Map(
                BuildPreview());

        string hasChanges =
            display.Summary
                .First(s => s.Label == "Has Changes").Value;

        Assert.Equal("No", hasChanges);
    }

    [Fact]
    public void Map_StepRowsExposeTargetAndReason()
    {
        ExecutionPreview preview = BuildPreview(
            CreateStep(
                ExecutionPreviewOperation.Create,
                ExecutionPreviewCategory.Route,
                "10.0.0.0/24",
                "Desired route missing"));

        ExecutionPreviewDisplay display =
            ExecutionPreviewDialogModel.Map(preview);

        ExecutionPreviewStepDetail detail =
            Assert.Single(display.Steps);

        Assert.Equal(
            "10.0.0.0/24", detail.Row.Target);
        Assert.Equal(
            "Desired route missing", detail.Row.Reason);
        Assert.Equal(
            ExecutionPreviewOperation.Create.ToString(),
            detail.Row.Operation);
        Assert.Equal(
            ExecutionPreviewCategory.Route.ToString(),
            detail.Row.Category);
    }

    [Fact]
    public void GetCopyText_EmptyPreview_NoChangesApplied()
    {
        string text =
            ExecutionPreviewDialogModel.GetCopyText(
                BuildPreview());

        Assert.Contains(
            "=== Execution Preview ===", text);
        Assert.Contains(
            "No changes would be applied.", text);
    }

    [Fact]
    public void GetCopyText_NonEmptyPreview_IncludesSummaryAndSteps()
    {
        ExecutionPreview preview = BuildPreview(
            CreateStep(
                ExecutionPreviewOperation.Create,
                ExecutionPreviewCategory.Route,
                "10.0.0.0/24",
                "Desired route missing"));

        string text =
            ExecutionPreviewDialogModel.GetCopyText(preview);

        Assert.Contains(
            "Routes to Create: 1", text);
        Assert.Contains(
            "Routes to Remove: 0", text);
        Assert.Contains(
            "Has Changes: Yes", text);
        Assert.Contains(
            "Steps:", text);
        Assert.Contains("10.0.0.0/24", text);
        Assert.Contains(
            "Desired route missing", text);
        Assert.DoesNotContain(
            "No changes would be applied.", text);
    }

    [Fact]
    public void GetCopyText_PreservesStepOrder()
    {
        ExecutionPreview preview = BuildPreview(
            CreateStep(
                ExecutionPreviewOperation.Create,
                ExecutionPreviewCategory.Route,
                "10.0.0.0/24",
                "alpha"),
            CreateStep(
                ExecutionPreviewOperation.Create,
                ExecutionPreviewCategory.Route,
                "10.0.1.0/24",
                "beta"),
            CreateStep(
                ExecutionPreviewOperation.Delete,
                ExecutionPreviewCategory.Route,
                "10.0.2.0/24",
                "gamma"));

        string text =
            ExecutionPreviewDialogModel.GetCopyText(preview);

        int alpha = text.IndexOf(
            "10.0.0.0/24", StringComparison.Ordinal);
        int beta = text.IndexOf(
            "10.0.1.0/24", StringComparison.Ordinal);
        int gamma = text.IndexOf(
            "10.0.2.0/24", StringComparison.Ordinal);

        Assert.True(alpha >= 0);
        Assert.True(beta > alpha);
        Assert.True(gamma > beta);
    }

    [Fact]
    public void GetCopyText_SamePreview_ProducesSameText()
    {
        DateTimeOffset capturedAt = new(
            2026, 8, 3, 10, 0, 0, TimeSpan.Zero);

        ExecutionPreview preview = BuildPreview(
            capturedAt,
            CreateStep(
                ExecutionPreviewOperation.Create,
                ExecutionPreviewCategory.Route,
                "10.0.0.0/24",
                "reason"));

        string first =
            ExecutionPreviewDialogModel.GetCopyText(preview);
        string second =
            ExecutionPreviewDialogModel.GetCopyText(preview);

        Assert.Equal(first, second);
    }

    [Fact]
    public void GetCopyText_NullPreview_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => ExecutionPreviewDialogModel.GetCopyText(
                null!));
    }

    private static ExecutionPreviewStep CreateStep(
        ExecutionPreviewOperation operation,
        ExecutionPreviewCategory category,
        string target,
        string reason = "reason") =>
        new()
        {
            Operation = operation,
            Category = category,
            Target = target,
            Reason = reason
        };
}
