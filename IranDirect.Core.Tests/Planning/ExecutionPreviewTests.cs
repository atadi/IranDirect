using IranDirect.Core.Planning;

namespace IranDirect.Core.Tests.Planning;

public sealed class ExecutionPreviewTests
{
    [Fact]
    public void Step_AllProperties_AreSet()
    {
        ExecutionPreviewStep step = new()
        {
            Category = ExecutionPreviewCategory.Route,
            Operation = ExecutionPreviewOperation.Create,
            Target = "10.0.0.0/8",
            Reason = "Route is missing"
        };

        Assert.Equal(
            ExecutionPreviewCategory.Route,
            step.Category);
        Assert.Equal(
            ExecutionPreviewOperation.Create,
            step.Operation);
        Assert.Equal("10.0.0.0/8", step.Target);
        Assert.Equal("Route is missing", step.Reason);
    }

    [Fact]
    public void Step_IsImmutable_Record()
    {
        ExecutionPreviewStep step = new()
        {
            Category = ExecutionPreviewCategory.Route,
            Operation = ExecutionPreviewOperation.Create,
            Target = "10.0.0.0/8",
            Reason = "missing"
        };

        ExecutionPreviewStep updated = step with
        {
            Target = "10.0.0.0/24"
        };

        Assert.Equal("10.0.0.0/24", updated.Target);
        Assert.Equal("10.0.0.0/8", step.Target);
    }

    [Fact]
    public void Summary_EstimatedOperations_SumsAllCounts()
    {
        ExecutionPreviewSummary summary = new()
        {
            CreateCount = 2,
            DeleteCount = 1,
            VerifyCount = 3,
            InventoryUpdates = 4,
            CustomRouteUpdates = 5,
            VpnEndpointUpdates = 1
        };

        Assert.Equal(16, summary.EstimatedOperations);
    }

    [Fact]
    public void Summary_EstimatedOperations_ZerosReturnZero()
    {
        ExecutionPreviewSummary summary = new()
        {
            CreateCount = 0,
            DeleteCount = 0,
            VerifyCount = 0,
            InventoryUpdates = 0,
            CustomRouteUpdates = 0,
            VpnEndpointUpdates = 0
        };

        Assert.Equal(0, summary.EstimatedOperations);
    }

    [Fact]
    public void Summary_HasChanges_TrueWhenCreatesPresent()
    {
        ExecutionPreviewSummary summary = new()
        {
            CreateCount = 1,
            DeleteCount = 0,
            VerifyCount = 0,
            InventoryUpdates = 0,
            CustomRouteUpdates = 0,
            VpnEndpointUpdates = 0
        };

        Assert.True(summary.HasChanges);
    }

    [Fact]
    public void Summary_HasChanges_TrueWhenDeletesPresent()
    {
        ExecutionPreviewSummary summary = new()
        {
            CreateCount = 0,
            DeleteCount = 1,
            VerifyCount = 0,
            InventoryUpdates = 0,
            CustomRouteUpdates = 0,
            VpnEndpointUpdates = 0
        };

        Assert.True(summary.HasChanges);
    }

    [Fact]
    public void Summary_HasChanges_TrueWhenInventoryUpdates()
    {
        ExecutionPreviewSummary summary = new()
        {
            CreateCount = 0,
            DeleteCount = 0,
            VerifyCount = 0,
            InventoryUpdates = 1,
            CustomRouteUpdates = 0,
            VpnEndpointUpdates = 0
        };

        Assert.True(summary.HasChanges);
    }

    [Fact]
    public void Summary_HasChanges_TrueWhenCustomRouteUpdates()
    {
        ExecutionPreviewSummary summary = new()
        {
            CreateCount = 0,
            DeleteCount = 0,
            VerifyCount = 0,
            InventoryUpdates = 0,
            CustomRouteUpdates = 1,
            VpnEndpointUpdates = 0
        };

        Assert.True(summary.HasChanges);
    }

    [Fact]
    public void Summary_HasChanges_TrueWhenVpnUpdates()
    {
        ExecutionPreviewSummary summary = new()
        {
            CreateCount = 0,
            DeleteCount = 0,
            VerifyCount = 0,
            InventoryUpdates = 0,
            CustomRouteUpdates = 0,
            VpnEndpointUpdates = 1
        };

        Assert.True(summary.HasChanges);
    }

    [Fact]
    public void Summary_HasChanges_FalseWhenOnlyVerifies()
    {
        ExecutionPreviewSummary summary = new()
        {
            CreateCount = 0,
            DeleteCount = 0,
            VerifyCount = 5,
            InventoryUpdates = 0,
            CustomRouteUpdates = 0,
            VpnEndpointUpdates = 0
        };

        Assert.False(summary.HasChanges);
    }

    [Fact]
    public void Summary_HasChanges_FalseWhenAllZero()
    {
        ExecutionPreviewSummary summary = new()
        {
            CreateCount = 0,
            DeleteCount = 0,
            VerifyCount = 0,
            InventoryUpdates = 0,
            CustomRouteUpdates = 0,
            VpnEndpointUpdates = 0
        };

        Assert.False(summary.HasChanges);
    }

    [Fact]
    public void Preview_HasChanges_DelegatesToSummary()
    {
        ExecutionPreviewSummary summaryWithChanges =
            new()
        {
            CreateCount = 1,
            DeleteCount = 0,
            VerifyCount = 0,
            InventoryUpdates = 0,
            CustomRouteUpdates = 0,
            VpnEndpointUpdates = 0
        };

        ExecutionPreview preview = new()
        {
            CapturedAt = DateTimeOffset.UtcNow,
            Summary = summaryWithChanges,
            Steps = []
        };

        Assert.True(preview.HasChanges);
    }

    [Fact]
    public void Preview_HasChanges_FalseWhenSummaryHasNoChanges()
    {
        ExecutionPreviewSummary summaryNoChanges = new()
        {
            CreateCount = 0,
            DeleteCount = 0,
            VerifyCount = 3,
            InventoryUpdates = 0,
            CustomRouteUpdates = 0,
            VpnEndpointUpdates = 0
        };

        ExecutionPreview preview = new()
        {
            CapturedAt = DateTimeOffset.UtcNow,
            Summary = summaryNoChanges,
            Steps = []
        };

        Assert.False(preview.HasChanges);
    }

    [Fact]
    public void Preview_Categories_GroupsStepsByCategory()
    {
        ExecutionPreview preview = new()
        {
            CapturedAt = DateTimeOffset.UtcNow,
            Summary = CreateSummary(3),
            Steps =
            [
                new ExecutionPreviewStep
                {
                    Category =
                        ExecutionPreviewCategory.Route,
                    Operation =
                        ExecutionPreviewOperation.Create,
                    Target = "10.0.0.0/8",
                    Reason = "Missing route"
                },
                new ExecutionPreviewStep
                {
                    Category =
                        ExecutionPreviewCategory
                            .CustomRoute,
                    Operation =
                        ExecutionPreviewOperation.Add,
                    Target = "example.com",
                    Reason = "New custom route"
                },
                new ExecutionPreviewStep
                {
                    Category =
                        ExecutionPreviewCategory.Route,
                    Operation =
                        ExecutionPreviewOperation.Verify,
                    Target = "192.168.0.0/16",
                    Reason = "Verify existing route"
                }
            ]
        };

        IReadOnlyDictionary<ExecutionPreviewCategory,
            IReadOnlyList<ExecutionPreviewStep>>
            categories = preview.Categories;

        Assert.Equal(2, categories.Count);
        Assert.Equal(2,
            categories[
                ExecutionPreviewCategory.Route].Count);
        Assert.Single(
            categories[
                ExecutionPreviewCategory.CustomRoute]);
    }

    [Fact]
    public void Preview_Categories_PreservesStepOrder()
    {
        ExecutionPreview preview = new()
        {
            CapturedAt = DateTimeOffset.UtcNow,
            Summary = CreateSummary(2),
            Steps =
            [
                new ExecutionPreviewStep
                {
                    Category =
                        ExecutionPreviewCategory.Route,
                    Operation =
                        ExecutionPreviewOperation.Create,
                    Target = "first",
                    Reason = "First step"
                },
                new ExecutionPreviewStep
                {
                    Category =
                        ExecutionPreviewCategory.Route,
                    Operation =
                        ExecutionPreviewOperation.Delete,
                    Target = "second",
                    Reason = "Second step"
                }
            ]
        };

        IReadOnlyList<ExecutionPreviewStep> routeSteps =
            preview.Categories[
                ExecutionPreviewCategory.Route];

        Assert.Equal("first", routeSteps[0].Target);
        Assert.Equal("second", routeSteps[1].Target);
    }

    [Fact]
    public void Preview_Categories_EmptySteps_ReturnsEmpty()
    {
        ExecutionPreview preview = new()
        {
            CapturedAt = DateTimeOffset.UtcNow,
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

        IReadOnlyDictionary<ExecutionPreviewCategory,
            IReadOnlyList<ExecutionPreviewStep>>
            categories = preview.Categories;

        Assert.Empty(categories);
    }

    [Fact]
    public void Preview_Steps_DefaultsToEmptyCollection()
    {
        ExecutionPreview preview = new()
        {
            CapturedAt = DateTimeOffset.UtcNow,
            Summary = new ExecutionPreviewSummary
            {
                CreateCount = 0,
                DeleteCount = 0,
                VerifyCount = 0,
                InventoryUpdates = 0,
                CustomRouteUpdates = 0,
                VpnEndpointUpdates = 0
            }
        };

        Assert.Empty(preview.Steps);
    }

    [Fact]
    public void Preview_CapturedAt_IsSetCorrectly()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        ExecutionPreview preview = new()
        {
            CapturedAt = now,
            Summary = new ExecutionPreviewSummary
            {
                CreateCount = 0,
                DeleteCount = 0,
                VerifyCount = 0,
                InventoryUpdates = 0,
                CustomRouteUpdates = 0,
                VpnEndpointUpdates = 0
            }
        };

        Assert.Equal(now, preview.CapturedAt);
    }

    [Fact]
    public void Preview_IsImmutable_Record()
    {
        ExecutionPreview original = new()
        {
            CapturedAt = DateTimeOffset.UtcNow,
            Summary = new ExecutionPreviewSummary
            {
                CreateCount = 1,
                DeleteCount = 0,
                VerifyCount = 0,
                InventoryUpdates = 0,
                CustomRouteUpdates = 0,
                VpnEndpointUpdates = 0
            },
            Steps =
            [
                new ExecutionPreviewStep
                {
                    Category =
                        ExecutionPreviewCategory.Route,
                    Operation =
                        ExecutionPreviewOperation.Create,
                    Target = "10.0.0.0/8",
                    Reason = "Missing"
                }
            ]
        };

        ExecutionPreview updated = original with
        {
            CapturedAt = original.CapturedAt.AddHours(1)
        };

        Assert.NotEqual(updated.CapturedAt,
            original.CapturedAt);
        Assert.Single(original.Steps);
        Assert.Single(updated.Steps);
    }

    [Fact]
    public void Step_IsImmutable_Record_VpnEndpoint()
    {
        ExecutionPreviewStep original = new()
        {
            Category = ExecutionPreviewCategory.VpnEndpoint,
            Operation = ExecutionPreviewOperation.Update,
            Target = "192.168.1.100",
            Reason = "Update VPN endpoint"
        };

        ExecutionPreviewStep updated = original with
        {
            Reason = "Updated reason"
        };

        Assert.Equal("Update VPN endpoint",
            original.Reason);
        Assert.Equal("Updated reason",
            updated.Reason);
    }

    [Fact]
    public void Summary_IsImmutable_Record()
    {
        ExecutionPreviewSummary original = new()
        {
            CreateCount = 1,
            DeleteCount = 0,
            VerifyCount = 0,
            InventoryUpdates = 0,
            CustomRouteUpdates = 0,
            VpnEndpointUpdates = 0
        };

        ExecutionPreviewSummary updated = original with
        {
            CreateCount = 5
        };

        Assert.Equal(1, original.CreateCount);
        Assert.Equal(5, updated.CreateCount);
    }

    [Fact]
    public void Preview_AllCategories_AreGrouped()
    {
        ExecutionPreview preview = new()
        {
            CapturedAt = DateTimeOffset.UtcNow,
            Summary = new ExecutionPreviewSummary
            {
                CreateCount = 0,
                DeleteCount = 0,
                VerifyCount = 4,
                InventoryUpdates = 0,
                CustomRouteUpdates = 0,
                VpnEndpointUpdates = 0
            },
            Steps =
            [
                new ExecutionPreviewStep
                {
                    Category =
                        ExecutionPreviewCategory.Route,
                    Operation =
                        ExecutionPreviewOperation.Verify,
                    Target = "r1",
                    Reason = "verify"
                },
                new ExecutionPreviewStep
                {
                    Category =
                        ExecutionPreviewCategory
                            .CustomRoute,
                    Operation =
                        ExecutionPreviewOperation.Verify,
                    Target = "c1",
                    Reason = "verify"
                },
                new ExecutionPreviewStep
                {
                    Category =
                        ExecutionPreviewCategory
                            .VpnEndpoint,
                    Operation =
                        ExecutionPreviewOperation.Verify,
                    Target = "v1",
                    Reason = "verify"
                },
                new ExecutionPreviewStep
                {
                    Category =
                        ExecutionPreviewCategory
                            .InventoryUpdate,
                    Operation =
                        ExecutionPreviewOperation.Verify,
                    Target = "i1",
                    Reason = "verify"
                }
            ]
        };

        IReadOnlyDictionary<ExecutionPreviewCategory,
            IReadOnlyList<ExecutionPreviewStep>>
            categories = preview.Categories;

        Assert.Equal(4, categories.Count);
        Assert.True(categories.ContainsKey(
            ExecutionPreviewCategory.Route));
        Assert.True(categories.ContainsKey(
            ExecutionPreviewCategory.CustomRoute));
        Assert.True(categories.ContainsKey(
            ExecutionPreviewCategory.VpnEndpoint));
        Assert.True(categories.ContainsKey(
            ExecutionPreviewCategory.InventoryUpdate));
    }

    [Fact]
    public void Preview_Computed_HasChangesFalse_WhenNoChangesInSteps()
    {
        ExecutionPreview preview = new()
        {
            CapturedAt = DateTimeOffset.UtcNow,
            Summary = new ExecutionPreviewSummary
            {
                CreateCount = 0,
                DeleteCount = 0,
                VerifyCount = 0,
                InventoryUpdates = 0,
                CustomRouteUpdates = 0,
                VpnEndpointUpdates = 0
            },
            Steps =
            [
                new ExecutionPreviewStep
                {
                    Category =
                        ExecutionPreviewCategory.Route,
                    Operation =
                        ExecutionPreviewOperation.Verify,
                    Target = "10.0.0.0/8",
                    Reason = "Verify route"
                }
            ]
        };

        Assert.False(preview.HasChanges);
        Assert.Single(preview.Steps);
    }

    [Fact]
    public void Preview_HasChanges_True_WhenSummaryHasChanges()
    {
        ExecutionPreview preview = new()
        {
            CapturedAt = DateTimeOffset.UtcNow,
            Summary = new ExecutionPreviewSummary
            {
                CreateCount = 2,
                DeleteCount = 1,
                VerifyCount = 0,
                InventoryUpdates = 0,
                CustomRouteUpdates = 0,
                VpnEndpointUpdates = 0
            },
            Steps =
            [
                new ExecutionPreviewStep
                {
                    Category =
                        ExecutionPreviewCategory.Route,
                    Operation =
                        ExecutionPreviewOperation.Create,
                    Target = "10.0.0.0/8",
                    Reason = "Missing"
                },
                new ExecutionPreviewStep
                {
                    Category =
                        ExecutionPreviewCategory.Route,
                    Operation =
                        ExecutionPreviewOperation.Create,
                    Target = "192.168.0.0/16",
                    Reason = "Missing"
                },
                new ExecutionPreviewStep
                {
                    Category =
                        ExecutionPreviewCategory.Route,
                    Operation =
                        ExecutionPreviewOperation.Delete,
                    Target = "172.16.0.0/12",
                    Reason = "Stale route"
                }
            ]
        };

        Assert.True(preview.HasChanges);
        Assert.Equal(3, preview.Steps.Count);
    }

    private static ExecutionPreviewSummary CreateSummary(
        int totalSteps) =>
        new()
        {
            CreateCount = totalSteps,
            DeleteCount = 0,
            VerifyCount = 0,
            InventoryUpdates = 0,
            CustomRouteUpdates = 0,
            VpnEndpointUpdates = 0
        };
}
