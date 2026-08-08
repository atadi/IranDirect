namespace PathVeer.Core.Tests.Runtime.Execution;

using System.Net;
using PathVeer.Core.Runtime.Execution;
using PathVeer.Core.Runtime.Profiling;
using PathVeer.Core.Routing;
using PathVeer.Core.Vpn;

public sealed class WindowsRuntimeExecutionStepHandlerTests
{
    private static readonly RuntimeExecutionStep AddPrefixStep = new()
    {
        Kind = RuntimeExecutionStepKind.AddPrefixRoute,
        Identity = "203.0.113.0/24|192.168.1.1|10",
        DestinationPrefix = "203.0.113.0/24",
        Gateway = "192.168.1.1",
        InterfaceIndex = 10,
        Metric = 256,
        Description = "Add test prefix."
    };

    private static readonly RuntimeExecutionStep RemovePrefixStep = AddPrefixStep with
    {
        Kind = RuntimeExecutionStepKind.RemovePrefixRoute,
        Description = "Remove test prefix."
    };

    private static readonly RuntimeExecutionStep AddEndpointStep = new()
    {
        Kind = RuntimeExecutionStepKind.AddEndpointRoute,
        Identity = "10.0.0.1/32|192.168.1.1|10",
        DestinationPrefix = "10.0.0.1/32",
        Gateway = "192.168.1.1",
        InterfaceIndex = 10,
        Metric = 1,
        Description = "vpn.example.com"
    };

    private static readonly RuntimeExecutionStep RemoveEndpointStep = AddEndpointStep with
    {
        Kind = RuntimeExecutionStepKind.RemoveEndpointRoute,
        Description = "Remove test endpoint."
    };

    [Fact]
    public async Task AddPrefixGroup_MutatesVerifiesAndPersists()
    {
        FakeRouteManager routes = new();
        routes.Present.Remove("203.0.113.0/24|192.168.1.1|10");
        FakeRouteInventory routeInv = new();
        FakeEndpointInventory endpointInv = new();
        WindowsRuntimeExecutionStepHandler h = CreateHandler(routes, routeInv, endpointInv);

        IReadOnlyList<RuntimeExecutionStepResult> r =
            await ExecutePrefixGroupAsync(h, AddPrefixStep);

        Assert.Equal(RuntimeExecutionStepStatus.Succeeded, r[0].Status);
        Assert.Equal(1, routes.AddCallCount);
        Assert.Equal(1, routes.GetCallCount);
        RouteInventory saved = await routeInv.LoadAsync();
        Assert.Single(saved.Routes);
        Assert.Equal(AddPrefixStep.Identity, saved.Routes[0].Identity);
    }

    [Fact]
    public async Task AddPrefixGroup_OneSnapshotForManySteps()
    {
        FakeRouteManager routes = new();
        routes.Present.Clear();
        FakeRouteInventory routeInv = new();
        FakeEndpointInventory endpointInv = new();
        WindowsRuntimeExecutionStepHandler h = CreateHandler(routes, routeInv, endpointInv);

        RuntimeExecutionStep[] steps = Enumerable.Range(0, 4)
            .Select(i => AddPrefixStep with
            {
                Identity = $"203.0.113.{i}/24|192.168.1.1|{10 + i}",
                DestinationPrefix = $"203.0.113.{i}/24",
                InterfaceIndex = (uint)(10 + i)
            })
            .ToArray();

        IReadOnlyList<RuntimeExecutionStepResult> r =
            await ExecutePrefixGroupAsync(h, steps);

        Assert.All(r, rr => Assert.Equal(RuntimeExecutionStepStatus.Succeeded, rr.Status));
        Assert.Equal(4, routes.AddCallCount);
        Assert.Equal(1, routes.GetCallCount);
        RouteInventory saved = await routeInv.LoadAsync();
        Assert.Equal(4, saved.Routes.Count);
    }

    [Fact]
    public async Task AddPrefixGroup_VerificationFailure_NoOwnershipPersisted()
    {
        FakeRouteManager routes = new();
        routes.Present.Remove("203.0.113.0/24|192.168.1.1|10");
        routes.DontAdd.Add("203.0.113.0/24|192.168.1.1|10");
        FakeRouteInventory routeInv = new();
        FakeEndpointInventory endpointInv = new();
        WindowsRuntimeExecutionStepHandler h = CreateHandler(routes, routeInv, endpointInv);

        IReadOnlyList<RuntimeExecutionStepResult> r =
            await ExecutePrefixGroupAsync(h, AddPrefixStep);

        Assert.Equal(RuntimeExecutionStepStatus.Failed, r[0].Status);
        Assert.Contains("not found", r[0].ErrorMessage!, StringComparison.OrdinalIgnoreCase);
        RouteInventory saved = await routeInv.LoadAsync();
        Assert.Empty(saved.Routes);
    }

    [Fact]
    public async Task AddPrefixGroup_MutationFailure_ReturnsFailed()
    {
        FakeRouteManager routes = new(throwOnAdd: true);
        routes.Present.Remove("203.0.113.0/24|192.168.1.1|10");
        FakeRouteInventory routeInv = new();
        FakeEndpointInventory endpointInv = new();
        WindowsRuntimeExecutionStepHandler h = CreateHandler(routes, routeInv, endpointInv);

        IReadOnlyList<RuntimeExecutionStepResult> r =
            await ExecutePrefixGroupAsync(h, AddPrefixStep);

        Assert.Equal(RuntimeExecutionStepStatus.Failed, r[0].Status);
        Assert.Contains("failed", r[0].ErrorMessage!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, routes.GetCallCount);
    }

    [Fact]
    public async Task AddPrefixGroup_AlreadyExistsOwned_ResolvedSucceeded()
    {
        FakeRouteManager routes = new();
        routes.Present.Clear();
        routes.Present.Add("203.0.113.0/24|192.168.1.1|10");
        FakeRouteInventory routeInv = new();
        routeInv.Seed(AddPrefixStep.Identity);
        FakeEndpointInventory endpointInv = new();
        WindowsRuntimeExecutionStepHandler h = CreateHandler(routes, routeInv, endpointInv);

        IReadOnlyList<RuntimeExecutionStepResult> r =
            await ExecutePrefixGroupAsync(h, AddPrefixStep);

        Assert.Equal(RuntimeExecutionStepStatus.Succeeded, r[0].Status);
        Assert.Equal(1, routes.GetCallCount);
        RouteInventory saved = await routeInv.LoadAsync();
        Assert.Single(saved.Routes);
        Assert.Equal(AddPrefixStep.Identity, saved.Routes[0].Identity);
    }

    [Fact]
    public async Task AddPrefixGroup_AlreadyExistsUnowned_ReturnsFailedAndDoesNotClaim()
    {
        FakeRouteManager routes = new();
        routes.Present.Clear();
        routes.Present.Add("203.0.113.0/24|192.168.1.1|10");
        FakeRouteInventory routeInv = new();
        FakeEndpointInventory endpointInv = new();
        WindowsRuntimeExecutionStepHandler h = CreateHandler(routes, routeInv, endpointInv);

        IReadOnlyList<RuntimeExecutionStepResult> r =
            await ExecutePrefixGroupAsync(h, AddPrefixStep);

        Assert.Equal(RuntimeExecutionStepStatus.Failed, r[0].Status);
        Assert.Contains("not owned by IranDirect", r[0].ErrorMessage!, StringComparison.OrdinalIgnoreCase);
        RouteInventory saved = await routeInv.LoadAsync();
        Assert.Empty(saved.Routes);
    }

    [Fact]
    public async Task AddPrefixGroup_PreexistingOwned_IdempotentSuccess()
    {
        FakeRouteManager routes = new();
        routes.Present.Clear();
        routes.Present.Add("203.0.113.0/24|192.168.1.1|10");
        FakeRouteInventory routeInv = new();
        routeInv.Seed(AddPrefixStep.Identity);
        FakeEndpointInventory endpointInv = new();
        WindowsRuntimeExecutionStepHandler h = CreateHandler(routes, routeInv, endpointInv);

        IReadOnlyList<RuntimeExecutionStepResult> r =
            await ExecutePrefixGroupAsync(h, AddPrefixStep);

        Assert.Equal(RuntimeExecutionStepStatus.Succeeded, r[0].Status);
        Assert.Equal(0, routes.DeleteCallCount);
        RouteInventory saved = await routeInv.LoadAsync();
        Assert.Single(saved.Routes);
    }

    [Fact]
    public async Task AddPrefixGroup_PreexistingUnowned_ReturnsFailed()
    {
        FakeRouteManager routes = new();
        routes.Present.Clear();
        routes.Present.Add("203.0.113.0/24|192.168.1.1|10");
        FakeRouteInventory routeInv = new();
        FakeEndpointInventory endpointInv = new();
        WindowsRuntimeExecutionStepHandler h = CreateHandler(routes, routeInv, endpointInv);

        IReadOnlyList<RuntimeExecutionStepResult> r =
            await ExecutePrefixGroupAsync(h, AddPrefixStep);

        Assert.Equal(RuntimeExecutionStepStatus.Failed, r[0].Status);
        Assert.Contains("not owned by IranDirect", r[0].ErrorMessage!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AddPrefixGroup_ExactMatchRequiresAllFourFields()
    {
        FakeRouteManager routes = new();
        routes.Present.Clear();
        routes.Present.Add("203.0.113.0/24|192.168.1.1|10");
        routes.RouteMetrics["203.0.113.0/24|192.168.1.1|10"] = 100;
        FakeRouteInventory routeInv = new();
        FakeEndpointInventory endpointInv = new();
        WindowsRuntimeExecutionStepHandler h = CreateHandler(routes, routeInv, endpointInv);

        IReadOnlyList<RuntimeExecutionStepResult> r =
            await ExecutePrefixGroupAsync(h, AddPrefixStep);

        Assert.Equal(RuntimeExecutionStepStatus.Failed, r[0].Status);
        RouteInventory saved = await routeInv.LoadAsync();
        Assert.Empty(saved.Routes);
    }

    [Fact]
    public async Task AddPrefixGroup_MixedOutcomes_ResultsInInputOrder()
    {
        FakeRouteManager routes = new();
        routes.Present.Clear();
        FakeRouteInventory routeInv = new();
        FakeEndpointInventory endpointInv = new();
        WindowsRuntimeExecutionStepHandler h = CreateHandler(routes, routeInv, endpointInv);

        RuntimeExecutionStep[] steps =
        [
            AddPrefixStep with
            {
                Identity = "203.0.113.0/24|192.168.1.1|10",
                DestinationPrefix = "203.0.113.0/24",
                InterfaceIndex = 10
            },
            AddPrefixStep with
            {
                Identity = "203.0.113.1/24|192.168.1.1|11",
                DestinationPrefix = "203.0.113.1/24",
                InterfaceIndex = 11
            },
            AddPrefixStep with
            {
                Identity = "203.0.113.2/24|192.168.1.1|12",
                DestinationPrefix = "203.0.113.2/24",
                InterfaceIndex = 12
            }
        ];
        routes.DontAdd.Add("203.0.113.1/24|192.168.1.1|11");

        IReadOnlyList<RuntimeExecutionStepResult> r =
            await ExecutePrefixGroupAsync(h, steps);

        Assert.Equal(RuntimeExecutionStepStatus.Succeeded, r[0].Status);
        Assert.Equal(RuntimeExecutionStepStatus.Failed, r[1].Status);
        Assert.Equal(RuntimeExecutionStepStatus.Succeeded, r[2].Status);
        Assert.Equal(steps[0].Identity, r[0].StepIdentity);
        Assert.Equal(steps[1].Identity, r[1].StepIdentity);
        Assert.Equal(steps[2].Identity, r[2].StepIdentity);
        Assert.Equal(1, routes.GetCallCount);

        RouteInventory saved = await routeInv.LoadAsync();
        Assert.Equal(2, saved.Routes.Count);
        Assert.DoesNotContain("203.0.113.1/24|192.168.1.1|11", saved.Routes.Select(x => x.Identity));
    }

    [Fact]
    public async Task AddPrefixGroup_InventoryFailure_CompensatesSuccessfully()
    {
        FakeRouteManager routes = new();
        routes.Present.Remove("203.0.113.0/24|192.168.1.1|10");
        routes.TrackDeletions = true;
        FakeRouteInventory routeInv = new(throwOnSave: true);
        FakeEndpointInventory endpointInv = new();
        WindowsRuntimeExecutionStepHandler h = CreateHandler(routes, routeInv, endpointInv);

        IReadOnlyList<RuntimeExecutionStepResult> r =
            await ExecutePrefixGroupAsync(h, AddPrefixStep);

        Assert.Equal(RuntimeExecutionStepStatus.Failed, r[0].Status);
        Assert.Contains("removed as compensation", r[0].ErrorMessage!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, routes.AddCallCount);
        Assert.Equal(1, routes.DeleteCallCount);
        Assert.DoesNotContain(AddPrefixStep.Identity, routes.Present);
    }

    [Fact]
    public async Task AddPrefixGroup_InventoryFailure_OrphanReported()
    {
        FakeRouteManager routes = new();
        routes.Present.Remove("203.0.113.0/24|192.168.1.1|10");
        FakeRouteInventory routeInv = new(throwOnSave: true);
        FakeEndpointInventory endpointInv = new();
        WindowsRuntimeExecutionStepHandler h = CreateHandler(routes, routeInv, endpointInv);

        IReadOnlyList<RuntimeExecutionStepResult> r =
            await ExecutePrefixGroupAsync(h, AddPrefixStep);

        Assert.Equal(RuntimeExecutionStepStatus.Failed, r[0].Status);
        Assert.Contains("Orphaned route", r[0].ErrorMessage!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(AddPrefixStep.Identity, routes.Present);
    }

    [Fact]
    public async Task RemovePrefixGroup_OwnedRoute_RemovesVerifiesAndClearsOwnership()
    {
        FakeRouteManager routes = new();
        FakeRouteInventory routeInv = new();
        routeInv.Seed(AddPrefixStep.Identity);
        FakeEndpointInventory endpointInv = new();
        WindowsRuntimeExecutionStepHandler h = CreateHandler(routes, routeInv, endpointInv);

        routes.TrackDeletions = true;

        IReadOnlyList<RuntimeExecutionStepResult> r =
            await ExecutePrefixGroupAsync(h, RemovePrefixStep);

        Assert.Equal(RuntimeExecutionStepStatus.Succeeded, r[0].Status);
        Assert.Equal(1, routes.DeleteCallCount);
        Assert.Equal(1, routes.GetCallCount);
        RouteInventory saved = await routeInv.LoadAsync();
        Assert.Empty(saved.Routes);
    }

    [Fact]
    public async Task RemovePrefixGroup_OneSnapshotForManySteps()
    {
        FakeRouteManager routes = new();
        routes.TrackDeletions = true;
        FakeRouteInventory routeInv = new();
        FakeEndpointInventory endpointInv = new();
        WindowsRuntimeExecutionStepHandler h = CreateHandler(routes, routeInv, endpointInv);

        RuntimeExecutionStep[] steps = Enumerable.Range(0, 4)
            .Select(i => RemovePrefixStep with
            {
                Identity = $"203.0.113.{i}/24|192.168.1.1|{10 + i}",
                DestinationPrefix = $"203.0.113.{i}/24",
                InterfaceIndex = (uint)(10 + i)
            })
            .ToArray();

        foreach (RuntimeExecutionStep step in steps)
            routeInv.Seed(step.Identity, additive: true);

        IReadOnlyList<RuntimeExecutionStepResult> r =
            await ExecutePrefixGroupAsync(h, steps);

        Assert.All(r, rr => Assert.Equal(RuntimeExecutionStepStatus.Succeeded, rr.Status));
        Assert.Equal(4, routes.DeleteCallCount);
        Assert.Equal(1, routes.GetCallCount);
        RouteInventory saved = await routeInv.LoadAsync();
        Assert.Empty(saved.Routes);
    }

    [Fact]
    public async Task RemovePrefixGroup_NonOwnedRouteOnPlatform_ReturnsFailed()
    {
        FakeRouteManager routes = new();
        FakeRouteInventory routeInv = new();
        FakeEndpointInventory endpointInv = new();
        WindowsRuntimeExecutionStepHandler h = CreateHandler(routes, routeInv, endpointInv);

        IReadOnlyList<RuntimeExecutionStepResult> r =
            await ExecutePrefixGroupAsync(h, RemovePrefixStep);

        Assert.Equal(RuntimeExecutionStepStatus.Failed, r[0].Status);
        Assert.Contains("not owned by IranDirect", r[0].ErrorMessage!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, routes.DeleteCallCount);
    }

    [Fact]
    public async Task RemovePrefixGroup_RouteStillExistsAfterDelete_ReturnsFailedAndPreservesOwnership()
    {
        FakeRouteManager routes = new();
        FakeRouteInventory routeInv = new();
        routeInv.Seed(AddPrefixStep.Identity);
        FakeEndpointInventory endpointInv = new();
        WindowsRuntimeExecutionStepHandler h = CreateHandler(routes, routeInv, endpointInv);

        IReadOnlyList<RuntimeExecutionStepResult> r =
            await ExecutePrefixGroupAsync(h, RemovePrefixStep);

        Assert.Equal(RuntimeExecutionStepStatus.Failed, r[0].Status);
        Assert.Contains("still exists", r[0].ErrorMessage!, StringComparison.OrdinalIgnoreCase);
        RouteInventory saved = await routeInv.LoadAsync();
        Assert.NotEmpty(saved.Routes);
    }

    [Fact]
    public async Task RemovePrefixGroup_ExactRemoveUsesIdentityRegardlessOfMetric()
    {
        FakeRouteManager routes = new();
        routes.Present.Clear();
        routes.Present.Add("203.0.113.0/24|192.168.1.1|10");
        routes.RouteMetrics["203.0.113.0/24|192.168.1.1|10"] = 100;
        routes.TrackDeletions = true;
        FakeRouteInventory routeInv = new();
        routeInv.Seed(AddPrefixStep.Identity);
        FakeEndpointInventory endpointInv = new();
        WindowsRuntimeExecutionStepHandler h = CreateHandler(routes, routeInv, endpointInv);

        IReadOnlyList<RuntimeExecutionStepResult> r =
            await ExecutePrefixGroupAsync(h, RemovePrefixStep);

        Assert.Equal(RuntimeExecutionStepStatus.Succeeded, r[0].Status);
        RouteInventory saved = await routeInv.LoadAsync();
        Assert.Empty(saved.Routes);
    }

    [Fact]
    public async Task RemovePrefixGroup_AlreadyAbsentWithStaleInventory_RemovesStaleAndSucceeds()
    {
        FakeRouteManager routes = new();
        routes.Present.Remove("203.0.113.0/24|192.168.1.1|10");
        FakeRouteInventory routeInv = new();
        routeInv.Seed(AddPrefixStep.Identity);
        FakeEndpointInventory endpointInv = new();
        WindowsRuntimeExecutionStepHandler h = CreateHandler(routes, routeInv, endpointInv);

        IReadOnlyList<RuntimeExecutionStepResult> r =
            await ExecutePrefixGroupAsync(h, RemovePrefixStep);

        Assert.Equal(RuntimeExecutionStepStatus.Succeeded, r[0].Status);
        RouteInventory saved = await routeInv.LoadAsync();
        Assert.Empty(saved.Routes);
    }

    [Fact]
    public async Task RemovePrefixGroup_AlreadyAbsentNoInventory_NoopSucceeds()
    {
        FakeRouteManager routes = new();
        routes.Present.Remove("203.0.113.0/24|192.168.1.1|10");
        FakeRouteInventory routeInv = new();
        FakeEndpointInventory endpointInv = new();
        WindowsRuntimeExecutionStepHandler h = CreateHandler(routes, routeInv, endpointInv);

        IReadOnlyList<RuntimeExecutionStepResult> r =
            await ExecutePrefixGroupAsync(h, RemovePrefixStep);

        Assert.Equal(RuntimeExecutionStepStatus.Succeeded, r[0].Status);
        Assert.Equal(0, routes.DeleteCallCount);
    }

    [Fact]
    public async Task RemovePrefixGroup_InventoryFailureAfterVerify_StepFails()
    {
        FakeRouteManager routes = new();
        routes.TrackDeletions = true;
        FakeRouteInventory routeInv = new(throwOnSave: true);
        routeInv.Seed(AddPrefixStep.Identity);
        FakeEndpointInventory endpointInv = new();
        WindowsRuntimeExecutionStepHandler h = CreateHandler(routes, routeInv, endpointInv);

        IReadOnlyList<RuntimeExecutionStepResult> r =
            await ExecutePrefixGroupAsync(h, RemovePrefixStep);

        Assert.Equal(RuntimeExecutionStepStatus.Failed, r[0].Status);
        Assert.Contains("inventory", r[0].ErrorMessage!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RemovePrefixGroup_InventoryOnlyForVerifiedRemovals()
    {
        FakeRouteManager routes = new();
        routes.TrackDeletions = true;
        FakeRouteInventory routeInv = new();
        FakeEndpointInventory endpointInv = new();
        WindowsRuntimeExecutionStepHandler h = CreateHandler(routes, routeInv, endpointInv);

        RuntimeExecutionStep ok = RemovePrefixStep with
        {
            Identity = "203.0.113.0/24|192.168.1.1|10",
            DestinationPrefix = "203.0.113.0/24",
            InterfaceIndex = 10
        };
        RuntimeExecutionStep stuck = RemovePrefixStep with
        {
            Identity = "203.0.113.1/24|192.168.1.1|11",
            DestinationPrefix = "203.0.113.1/24",
            InterfaceIndex = 11
        };
        routeInv.Seed(ok.Identity);
        routeInv.Seed(stuck.Identity, additive: true);
        routes.Present.Add(stuck.Identity);
        routes.DoNotDelete.Add(stuck.Identity);

        IReadOnlyList<RuntimeExecutionStepResult> r =
            await ExecutePrefixGroupAsync(h, ok, stuck);

        Assert.Equal(RuntimeExecutionStepStatus.Succeeded, r[0].Status);
        Assert.Equal(RuntimeExecutionStepStatus.Failed, r[1].Status);
        RouteInventory saved = await routeInv.LoadAsync();
        RouteInventoryItem remaining = Assert.Single(saved.Routes);
        Assert.Equal(stuck.Identity, remaining.Identity);
    }

    [Fact]
    public async Task AddEndpoint_MutatesVerifiesAndPersists()
    {
        FakeRouteManager routes = new();
        routes.Present.Remove("10.0.0.1/32|192.168.1.1|10");
        FakeRouteInventory routeInv = new();
        FakeEndpointInventory endpointInv = new();
        WindowsRuntimeExecutionStepHandler h = CreateHandler(routes, routeInv, endpointInv);

        RuntimeExecutionStepResult r = await h.ExecuteAndVerifyAsync(AddEndpointStep);

        Assert.Equal(RuntimeExecutionStepStatus.Succeeded, r.Status);
        Assert.Equal(1, routes.AddCallCount);
        VpnEndpointInventory saved = await endpointInv.LoadAsync();
        Assert.Single(saved.Endpoints);
        Assert.Equal(AddEndpointStep.Identity, saved.Endpoints[0].Identity);
        Assert.True(saved.Endpoints[0].AddedByIranDirect);
    }

    [Fact]
    public async Task AddEndpoint_VerificationFailure_NoOwnershipPersisted()
    {
        FakeRouteManager routes = new();
        routes.Present.Remove("10.0.0.1/32|192.168.1.1|10");
        routes.SuppressRouteAdd = true;
        FakeRouteInventory routeInv = new();
        FakeEndpointInventory endpointInv = new();
        WindowsRuntimeExecutionStepHandler h = CreateHandler(routes, routeInv, endpointInv);

        RuntimeExecutionStepResult r = await h.ExecuteAndVerifyAsync(AddEndpointStep);

        Assert.Equal(RuntimeExecutionStepStatus.Failed, r.Status);
        Assert.Contains("not found", r.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
        VpnEndpointInventory saved = await endpointInv.LoadAsync();
        Assert.Empty(saved.Endpoints);
    }

    [Fact]
    public async Task RemoveEndpoint_RejectsNonOwnedRoute()
    {
        FakeRouteManager routes = new();
        FakeRouteInventory routeInv = new();
        FakeEndpointInventory endpointInv = new();
        endpointInv.SeedOwned(AddEndpointStep.Identity, addedByIranDirect: false);
        WindowsRuntimeExecutionStepHandler h = CreateHandler(routes, routeInv, endpointInv);

        RuntimeExecutionStepResult r = await h.ExecuteAndVerifyAsync(RemoveEndpointStep);

        Assert.Equal(RuntimeExecutionStepStatus.Failed, r.Status);
        Assert.Contains("not created by IranDirect", r.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, routes.DeleteCallCount);
    }

    [Fact]
    public async Task RemoveEndpoint_RejectsRouteNotInInventory()
    {
        FakeRouteManager routes = new();
        FakeRouteInventory routeInv = new();
        FakeEndpointInventory endpointInv = new();
        WindowsRuntimeExecutionStepHandler h = CreateHandler(routes, routeInv, endpointInv);

        RuntimeExecutionStepResult r = await h.ExecuteAndVerifyAsync(RemoveEndpointStep);

        Assert.Equal(RuntimeExecutionStepStatus.Failed, r.Status);
        Assert.Contains("not in the endpoint inventory", r.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, routes.DeleteCallCount);
    }

    [Fact]
    public async Task RemoveEndpoint_OwnedRoute_RemovesVerifiesAndClearsOwnership()
    {
        FakeRouteManager routes = new();
        FakeRouteInventory routeInv = new();
        FakeEndpointInventory endpointInv = new();
        endpointInv.SeedOwned(AddEndpointStep.Identity, addedByIranDirect: true);
        WindowsRuntimeExecutionStepHandler h = CreateHandler(routes, routeInv, endpointInv);

        routes.TrackDeletions = true;

        RuntimeExecutionStepResult r = await h.ExecuteAndVerifyAsync(RemoveEndpointStep);

        Assert.Equal(RuntimeExecutionStepStatus.Succeeded, r.Status);
        Assert.Equal(1, routes.DeleteCallCount);
        VpnEndpointInventory saved = await endpointInv.LoadAsync();
        Assert.Empty(saved.Endpoints);
    }

    [Fact]
    public async Task RemoveEndpoint_RouteStillExistsAfterDelete_ReturnsFailedAndPreservesOwnership()
    {
        FakeRouteManager routes = new();
        FakeRouteInventory routeInv = new();
        FakeEndpointInventory endpointInv = new();
        endpointInv.SeedOwned(AddEndpointStep.Identity, addedByIranDirect: true);
        WindowsRuntimeExecutionStepHandler h = CreateHandler(routes, routeInv, endpointInv);

        RuntimeExecutionStepResult r = await h.ExecuteAndVerifyAsync(RemoveEndpointStep);

        Assert.Equal(RuntimeExecutionStepStatus.Failed, r.Status);
        Assert.Contains("still exists", r.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
        VpnEndpointInventory saved = await endpointInv.LoadAsync();
        Assert.NotEmpty(saved.Endpoints);
    }

    [Fact]
    public async Task CancellationPropagates()
    {
        using CancellationTokenSource cts = new();
        FakeRouteManager routes = new();
        FakeRouteInventory routeInv = new();
        FakeEndpointInventory endpointInv = new();
        WindowsRuntimeExecutionStepHandler h = CreateHandler(routes, routeInv, endpointInv);

        cts.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => h.MutatePrefixRouteAsync(AddPrefixStep, cts.Token));
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => h.VerifyPrefixRouteGroupAsync(
                [AddPrefixStep], [PrefixMutationResult.Success()], cts.Token));
    }

    [Fact]
    public async Task InvalidStepGateway_Throws()
    {
        FakeRouteManager routes = new();
        FakeRouteInventory routeInv = new();
        FakeEndpointInventory endpointInv = new();
        WindowsRuntimeExecutionStepHandler h = CreateHandler(routes, routeInv, endpointInv);

        await Assert.ThrowsAsync<ArgumentException>(
            () => h.MutatePrefixRouteAsync(
                AddPrefixStep with { Gateway = "not-an-ip" }));
    }

    [Fact]
    public async Task AddPrefix_ExecuteAndVerify_DelegatesToGroupPath()
    {
        FakeRouteManager routes = new();
        routes.Present.Remove("203.0.113.0/24|192.168.1.1|10");
        FakeRouteInventory routeInv = new();
        FakeEndpointInventory endpointInv = new();
        WindowsRuntimeExecutionStepHandler h = CreateHandler(routes, routeInv, endpointInv);

        RuntimeExecutionStepResult r = await h.ExecuteAndVerifyAsync(AddPrefixStep);

        Assert.Equal(RuntimeExecutionStepStatus.Succeeded, r.Status);
        Assert.Equal(1, routes.GetCallCount);
        RouteInventory saved = await routeInv.LoadAsync();
        Assert.Single(saved.Routes);
    }

    [Fact]
    public async Task VerifyPrefixRouteGroup_RejectsMixedKinds()
    {
        FakeRouteManager routes = new();
        FakeRouteInventory routeInv = new();
        FakeEndpointInventory endpointInv = new();
        WindowsRuntimeExecutionStepHandler h = CreateHandler(routes, routeInv, endpointInv);

        await Assert.ThrowsAsync<ArgumentException>(
            () => h.VerifyPrefixRouteGroupAsync(
                [AddPrefixStep, RemovePrefixStep],
                [PrefixMutationResult.Success(), PrefixMutationResult.Success()]));
    }

    [Fact]
    public async Task VerifyPrefixRouteGroup_RejectsEndpointKinds()
    {
        FakeRouteManager routes = new();
        FakeRouteInventory routeInv = new();
        FakeEndpointInventory endpointInv = new();
        WindowsRuntimeExecutionStepHandler h = CreateHandler(routes, routeInv, endpointInv);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => h.VerifyPrefixRouteGroupAsync(
                [AddEndpointStep], [PrefixMutationResult.Success()]));
    }

    [Fact]
    public async Task VerifyPrefixRouteGroup_MismatchedMutationCount_Throws()
    {
        FakeRouteManager routes = new();
        FakeRouteInventory routeInv = new();
        FakeEndpointInventory endpointInv = new();
        WindowsRuntimeExecutionStepHandler h = CreateHandler(routes, routeInv, endpointInv);

        await Assert.ThrowsAsync<ArgumentException>(
            () => h.VerifyPrefixRouteGroupAsync(
                [AddPrefixStep, AddPrefixStep],
                [PrefixMutationResult.Success()]));
    }

    [Fact]
    public async Task PrefixGroup_RecordsProfilingCategories()
    {
        using TempDirectory temp = new();
        RuntimePerfReportStore store = new(temp.Path);
        RuntimeCycleProfiler profiler = new(enabled: true, store: store);

        FakeRouteManager routes = new();
        routes.Present.Remove("203.0.113.0/24|192.168.1.1|10");
        FakeRouteInventory routeInv = new();
        FakeEndpointInventory endpointInv = new();
        WindowsRuntimeExecutionStepHandler h = CreateHandler(routes, routeInv, endpointInv, profiler);

        using (profiler.BeginCycleIfNone("enable"))
        {
            IReadOnlyList<RuntimeExecutionStepResult> r =
                await ExecutePrefixGroupAsync(h, AddPrefixStep);

            Assert.Equal(RuntimeExecutionStepStatus.Succeeded, r[0].Status);
        }

        RuntimeCyclePerfReport? report = store.ReadLatest();

        Assert.NotNull(report);
        Assert.Contains(report.Categories,
            c => c.Category == RuntimePerfCategory.ExecutionPrefixAddMutation);
        Assert.Contains(report.Categories,
            c => c.Category == RuntimePerfCategory.ExecutionPrefixAddGroupVerification);
    }

    private static async Task<IReadOnlyList<RuntimeExecutionStepResult>> ExecutePrefixGroupAsync(
        WindowsRuntimeExecutionStepHandler handler,
        params RuntimeExecutionStep[] steps)
    {
        PrefixMutationResult[] mutations = new PrefixMutationResult[steps.Length];
        for (int i = 0; i < steps.Length; i++)
            mutations[i] = await handler.MutatePrefixRouteAsync(steps[i]);

        return await handler.VerifyPrefixRouteGroupAsync(steps, mutations);
    }

    private static WindowsRuntimeExecutionStepHandler CreateHandler(
        FakeRouteManager routes,
        FakeRouteInventory routeInv,
        FakeEndpointInventory endpointInv,
        RuntimeCycleProfiler? profiler = null)
    {
        return new WindowsRuntimeExecutionStepHandler(routes, routeInv, endpointInv, profiler);
    }

    private sealed class FakeRouteManager : IRouteManager
    {
        private readonly bool _throwOnAdd;

        public HashSet<string> Present { get; } = new(StringComparer.OrdinalIgnoreCase)
        {
            "203.0.113.0/24|192.168.1.1|10",
            "10.0.0.1/32|192.168.1.1|10"
        };

        public HashSet<string> DontAdd { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> DoNotDelete { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, int> RouteMetrics { get; } = new(StringComparer.OrdinalIgnoreCase);

        public int AddCallCount { get; private set; }
        public int DeleteCallCount { get; private set; }
        public int GetCallCount { get; private set; }
        public bool TrackDeletions { get; set; }
        public bool SuppressRouteAdd { get; set; }

        public FakeRouteManager(bool throwOnAdd = false)
        {
            _throwOnAdd = throwOnAdd;
        }

        public Task<IReadOnlyList<SystemRoute>> GetIpv4RoutesAsync(
            CancellationToken cancellationToken = default)
        {
            GetCallCount++;
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult<IReadOnlyList<SystemRoute>>(
                Present.Select(identity =>
                {
                    string[] p = identity.Split('|');
                    return new SystemRoute
                    {
                        DestinationPrefix = p[0],
                        NextHop = IPAddress.Parse(p[1]),
                        InterfaceIndex = uint.Parse(p[2]),
                        RouteMetric = RouteMetrics.GetValueOrDefault(identity, 256)
                    };
                }).ToArray());
        }

        public Task AddRoutesAsync(
            IReadOnlyCollection<ManagedRoute> routes,
            CancellationToken cancellationToken = default)
        {
            AddCallCount++;
            cancellationToken.ThrowIfCancellationRequested();
            if (_throwOnAdd) throw new InvalidOperationException("Add failed.");

            foreach (ManagedRoute r in routes)
            {
                if (Present.Contains(r.Identity))
                    throw new InvalidOperationException("The route already exists.");

                if (SuppressRouteAdd || DontAdd.Contains(r.Identity))
                    continue;

                Present.Add(r.Identity);
            }

            return Task.CompletedTask;
        }

        public Task DeleteRoutesAsync(
            IReadOnlyCollection<ManagedRoute> routes,
            CancellationToken cancellationToken = default)
        {
            DeleteCallCount++;
            cancellationToken.ThrowIfCancellationRequested();

            if (TrackDeletions)
            {
                foreach (ManagedRoute r in routes)
                {
                    if (!DoNotDelete.Contains(r.Identity))
                        Present.Remove(r.Identity);
                }
            }

            return Task.CompletedTask;
        }
    }

    private sealed class FakeRouteInventory : IRouteInventoryPersistence
    {
        private readonly bool _throwOnSave;
        private RouteInventory? _stored = new();

        public FakeRouteInventory(bool throwOnSave = false) { _throwOnSave = throwOnSave; }

        public void Seed(string identity, bool additive = false)
        {
            string[] p = identity.Split('|');
            RouteInventoryItem item = new()
            {
                DestinationPrefix = p[0], Gateway = p[1],
                InterfaceIndex = uint.Parse(p[2]), Metric = 256
            };

            if (additive && _stored is not null)
            {
                _stored = _stored with
                {
                    Routes = [.. _stored.Routes, item]
                };
            }
            else
            {
                _stored = new RouteInventory { Routes = [item] };
            }
        }

        public Task<RouteInventory> LoadAsync(CancellationToken ct = default) =>
            Task.FromResult(_stored ?? new RouteInventory());

        public Task SaveAsync(RouteInventory value, CancellationToken ct = default)
        {
            if (_throwOnSave) throw new InvalidOperationException("Save failed.");
            _stored = value;
            return Task.CompletedTask;
        }

        public Task MutateAsync(
            Func<RouteInventory, RouteInventory> transform,
            CancellationToken ct = default)
        {
            if (_throwOnSave) throw new InvalidOperationException("Save failed.");
            RouteInventory current = _stored ?? new RouteInventory();
            _stored = transform(current);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeEndpointInventory : IEndpointInventoryPersistence
    {
        private readonly bool _throwOnSave;
        private VpnEndpointInventory? _stored = new();

        public FakeEndpointInventory(bool throwOnSave = false)
        {
            _throwOnSave = throwOnSave;
        }

        public void SeedOwned(string identity, bool addedByIranDirect)
        {
            string[] p = identity.Split('|');
            _stored = new VpnEndpointInventory
            {
                Endpoints =
                [
                    new VpnEndpointInventoryItem
                    {
                        Host = "test.example.com", Address = p[1], Port = 1194,
                        Protocol = "udp", DestinationPrefix = p[0], Gateway = p[1],
                        InterfaceIndex = uint.Parse(p[2]), Metric = 1,
                        AddedByIranDirect = addedByIranDirect, IsCurrent = true,
                        ProtectedAt = DateTimeOffset.UtcNow, LastSeenAt = DateTimeOffset.UtcNow
                    }
                ]
            };
        }

        public Task<VpnEndpointInventory> LoadAsync(CancellationToken ct = default) =>
            Task.FromResult(_stored ?? new VpnEndpointInventory());

        public Task SaveAsync(VpnEndpointInventory value, CancellationToken ct = default)
        {
            _stored = value;
            return Task.CompletedTask;
        }

        public Task MutateAsync(
            Func<VpnEndpointInventory, VpnEndpointInventory> transform,
            CancellationToken ct = default)
        {
            if (_throwOnSave) throw new InvalidOperationException("Save failed.");
            VpnEndpointInventory current = _stored ?? new VpnEndpointInventory();
            _stored = transform(current);
            return Task.CompletedTask;
        }
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } =
            System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "irandirect-tests-" + Guid.NewGuid().ToString("N"));

        public TempDirectory() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch
            {
            }
        }
    }
}
