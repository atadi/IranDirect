namespace IranDirect.Core.Tests.Runtime.Execution;

using System.Net;
using IranDirect.Core.Runtime.Execution;
using IranDirect.Core.Routing;
using IranDirect.Core.Vpn;

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
    public async Task AddPrefix_MutatesVerifiesAndPersists()
    {
        FakeRouteManager routes = new();
        routes.Present.Remove("203.0.113.0/24|192.168.1.1|10");
        FakeRouteInventory routeInv = new();
        FakeEndpointInventory endpointInv = new();
        WindowsRuntimeExecutionStepHandler h = CreateHandler(routes, routeInv, endpointInv);

        RuntimeExecutionStepResult r = await h.ExecuteAndVerifyAsync(AddPrefixStep);

        Assert.Equal(RuntimeExecutionStepStatus.Succeeded, r.Status);
        Assert.Equal(1, routes.AddCallCount);
        Assert.Equal(2, routes.GetCallCount);
        RouteInventory saved = await routeInv.LoadAsync();
        Assert.Single(saved.Routes);
        Assert.Equal(AddPrefixStep.Identity, saved.Routes[0].Identity);
    }

    [Fact]
    public async Task AddPrefix_VerificationFailure_NoOwnershipPersisted()
    {
        FakeRouteManager routes = new();
        routes.Present.Remove("203.0.113.0/24|192.168.1.1|10");
        routes.SuppressRouteAdd = true;
        FakeRouteInventory routeInv = new();
        FakeEndpointInventory endpointInv = new();
        WindowsRuntimeExecutionStepHandler h = CreateHandler(routes, routeInv, endpointInv);

        RuntimeExecutionStepResult r = await h.ExecuteAndVerifyAsync(AddPrefixStep);

        Assert.Equal(RuntimeExecutionStepStatus.Failed, r.Status);
        Assert.Contains("not found", r.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
        RouteInventory saved = await routeInv.LoadAsync();
        Assert.Empty(saved.Routes);
    }

    [Fact]
    public async Task AddPrefix_MutationFailure_ReturnsFailed()
    {
        FakeRouteManager routes = new(throwOnAdd: true);
        routes.Present.Remove("203.0.113.0/24|192.168.1.1|10");
        FakeRouteInventory routeInv = new();
        FakeEndpointInventory endpointInv = new();
        WindowsRuntimeExecutionStepHandler h = CreateHandler(routes, routeInv, endpointInv);

        RuntimeExecutionStepResult r = await h.ExecuteAndVerifyAsync(AddPrefixStep);

        Assert.Equal(RuntimeExecutionStepStatus.Failed, r.Status);
        Assert.Contains("failed", r.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, routes.GetCallCount);
    }

    [Fact]
    public async Task RemovePrefix_OwnedRoute_RemovesVerifiesAndClearsOwnership()
    {
        FakeRouteManager routes = new();
        FakeRouteInventory routeInv = new();
        routeInv.Seed(AddPrefixStep.Identity);
        FakeEndpointInventory endpointInv = new();
        WindowsRuntimeExecutionStepHandler h = CreateHandler(routes, routeInv, endpointInv);

        routes.TrackDeletions = true;

        RuntimeExecutionStepResult r = await h.ExecuteAndVerifyAsync(RemovePrefixStep);

        Assert.Equal(RuntimeExecutionStepStatus.Succeeded, r.Status);
        Assert.Equal(1, routes.DeleteCallCount);
        RouteInventory saved = await routeInv.LoadAsync();
        Assert.Empty(saved.Routes);
    }

    [Fact]
    public async Task RemovePrefix_NonOwnedRouteOnPlatform_ReturnsFailed()
    {
        FakeRouteManager routes = new();
        FakeRouteInventory routeInv = new();
        FakeEndpointInventory endpointInv = new();
        WindowsRuntimeExecutionStepHandler h = CreateHandler(routes, routeInv, endpointInv);

        RuntimeExecutionStepResult r = await h.ExecuteAndVerifyAsync(RemovePrefixStep);

        Assert.Equal(RuntimeExecutionStepStatus.Failed, r.Status);
        Assert.Contains("not owned by IranDirect", r.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, routes.DeleteCallCount);
    }

    [Fact]
    public async Task RemovePrefix_RouteStillExistsAfterDelete_ReturnsFailedAndPreservesOwnership()
    {
        FakeRouteManager routes = new();
        FakeRouteInventory routeInv = new();
        routeInv.Seed(AddPrefixStep.Identity);
        FakeEndpointInventory endpointInv = new();
        WindowsRuntimeExecutionStepHandler h = CreateHandler(routes, routeInv, endpointInv);

        RuntimeExecutionStepResult r = await h.ExecuteAndVerifyAsync(RemovePrefixStep);

        Assert.Equal(RuntimeExecutionStepStatus.Failed, r.Status);
        Assert.Contains("still exists", r.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
        RouteInventory saved = await routeInv.LoadAsync();
        Assert.NotEmpty(saved.Routes);
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
            () => h.ExecuteAndVerifyAsync(AddPrefixStep, cts.Token));
    }

    [Fact]
    public async Task InvalidStepGateway_Throws()
    {
        FakeRouteManager routes = new();
        FakeRouteInventory routeInv = new();
        FakeEndpointInventory endpointInv = new();
        WindowsRuntimeExecutionStepHandler h = CreateHandler(routes, routeInv, endpointInv);

        await Assert.ThrowsAsync<ArgumentException>(
            () => h.ExecuteAndVerifyAsync(AddPrefixStep with { Gateway = "not-an-ip" }));
    }

    [Fact]
    public async Task AddPrefix_InventoryFailureAfterVerify_StepFails()
    {
        FakeRouteManager routes = new();
        routes.Present.Remove("203.0.113.0/24|192.168.1.1|10");
        FakeRouteInventory routeInv = new(throwOnSave: true);
        FakeEndpointInventory endpointInv = new();
        WindowsRuntimeExecutionStepHandler h = CreateHandler(routes, routeInv, endpointInv);

        RuntimeExecutionStepResult r = await h.ExecuteAndVerifyAsync(AddPrefixStep);

        Assert.Equal(RuntimeExecutionStepStatus.Failed, r.Status);
        Assert.Contains("inventory", r.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RemovePrefix_InventoryFailureAfterVerify_StepFails()
    {
        FakeRouteManager routes = new();
        FakeRouteInventory routeInv = new(throwOnSave: true);
        routeInv.Seed(AddPrefixStep.Identity);
        FakeEndpointInventory endpointInv = new();
        WindowsRuntimeExecutionStepHandler h = CreateHandler(routes, routeInv, endpointInv);

        routes.TrackDeletions = true;

        RuntimeExecutionStepResult r = await h.ExecuteAndVerifyAsync(RemovePrefixStep);

        Assert.Equal(RuntimeExecutionStepStatus.Failed, r.Status);
        Assert.Contains("inventory", r.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RemovePrefix_AlreadyAbsentWithStaleInventory_RemovesStaleAndSucceeds()
    {
        FakeRouteManager routes = new();
        routes.Present.Remove("203.0.113.0/24|192.168.1.1|10");
        FakeRouteInventory routeInv = new();
        routeInv.Seed(AddPrefixStep.Identity);
        FakeEndpointInventory endpointInv = new();
        WindowsRuntimeExecutionStepHandler h = CreateHandler(routes, routeInv, endpointInv);

        RuntimeExecutionStepResult r = await h.ExecuteAndVerifyAsync(RemovePrefixStep);

        Assert.Equal(RuntimeExecutionStepStatus.Succeeded, r.Status);
        RouteInventory saved = await routeInv.LoadAsync();
        Assert.Empty(saved.Routes);
        Assert.Equal(0, routes.DeleteCallCount);
    }

    [Fact]
    public async Task RemovePrefix_AlreadyAbsentNoInventory_NoopSucceeds()
    {
        FakeRouteManager routes = new();
        routes.Present.Remove("203.0.113.0/24|192.168.1.1|10");
        FakeRouteInventory routeInv = new();
        FakeEndpointInventory endpointInv = new();
        WindowsRuntimeExecutionStepHandler h = CreateHandler(routes, routeInv, endpointInv);

        RuntimeExecutionStepResult r = await h.ExecuteAndVerifyAsync(RemovePrefixStep);

        Assert.Equal(RuntimeExecutionStepStatus.Succeeded, r.Status);
        Assert.Equal(0, routes.DeleteCallCount);
    }

    [Fact]
    public async Task RemoveEndpoint_AlreadyAbsentWithStaleOwnedInventory_RemovesStaleAndSucceeds()
    {
        FakeRouteManager routes = new();
        routes.Present.Remove("10.0.0.1/32|192.168.1.1|10");
        FakeRouteInventory routeInv = new();
        FakeEndpointInventory endpointInv = new();
        endpointInv.SeedOwned(AddEndpointStep.Identity, addedByIranDirect: true);
        WindowsRuntimeExecutionStepHandler h = CreateHandler(routes, routeInv, endpointInv);

        RuntimeExecutionStepResult r = await h.ExecuteAndVerifyAsync(RemoveEndpointStep);

        Assert.Equal(RuntimeExecutionStepStatus.Succeeded, r.Status);
        VpnEndpointInventory saved = await endpointInv.LoadAsync();
        Assert.Empty(saved.Endpoints);
        Assert.Equal(0, routes.DeleteCallCount);
    }

    [Fact]
    public async Task RemoveEndpoint_AlreadyAbsentNoInventory_NoopSucceeds()
    {
        FakeRouteManager routes = new();
        routes.Present.Remove("10.0.0.1/32|192.168.1.1|10");
        FakeRouteInventory routeInv = new();
        FakeEndpointInventory endpointInv = new();
        WindowsRuntimeExecutionStepHandler h = CreateHandler(routes, routeInv, endpointInv);

        RuntimeExecutionStepResult r = await h.ExecuteAndVerifyAsync(RemoveEndpointStep);

        Assert.Equal(RuntimeExecutionStepStatus.Succeeded, r.Status);
        Assert.Equal(0, routes.DeleteCallCount);
    }

    [Fact]
    public async Task RemoveEndpoint_AlreadyAbsentWithNonOwnedInventory_PreservesEntry()
    {
        FakeRouteManager routes = new();
        routes.Present.Remove("10.0.0.1/32|192.168.1.1|10");
        FakeRouteInventory routeInv = new();
        FakeEndpointInventory endpointInv = new();
        endpointInv.SeedOwned(AddEndpointStep.Identity, addedByIranDirect: false);
        WindowsRuntimeExecutionStepHandler h = CreateHandler(routes, routeInv, endpointInv);

        RuntimeExecutionStepResult r = await h.ExecuteAndVerifyAsync(RemoveEndpointStep);

        Assert.Equal(RuntimeExecutionStepStatus.Succeeded, r.Status);
        VpnEndpointInventory saved = await endpointInv.LoadAsync();
        Assert.NotEmpty(saved.Endpoints);
        Assert.Equal(0, routes.DeleteCallCount);
    }

    [Fact]
    public async Task AddPrefix_PreexistingOwned_IdempotentSuccess()
    {
        FakeRouteManager routes = new();
        routes.Present.Clear();
        routes.Present.Add("203.0.113.0/24|192.168.1.1|10");
        FakeRouteInventory routeInv = new();
        routeInv.Seed(AddPrefixStep.Identity);
        FakeEndpointInventory endpointInv = new();
        WindowsRuntimeExecutionStepHandler h = CreateHandler(routes, routeInv, endpointInv);

        RuntimeExecutionStepResult r = await h.ExecuteAndVerifyAsync(AddPrefixStep);

        Assert.Equal(RuntimeExecutionStepStatus.Succeeded, r.Status);
        Assert.Equal(0, routes.AddCallCount);
        Assert.Equal(0, routes.DeleteCallCount);
    }

    [Fact]
    public async Task AddPrefix_PreexistingUnowned_ReturnsFailed()
    {
        FakeRouteManager routes = new();
        FakeRouteInventory routeInv = new();
        FakeEndpointInventory endpointInv = new();
        WindowsRuntimeExecutionStepHandler h = CreateHandler(routes, routeInv, endpointInv);

        RuntimeExecutionStepResult r = await h.ExecuteAndVerifyAsync(AddPrefixStep);

        Assert.Equal(RuntimeExecutionStepStatus.Failed, r.Status);
        Assert.Contains("not owned by IranDirect", r.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, routes.AddCallCount);
    }

    [Fact]
    public async Task AddPrefix_InventoryFailure_CompensatesSuccessfully()
    {
        FakeRouteManager routes = new();
        routes.Present.Remove("203.0.113.0/24|192.168.1.1|10");
        routes.TrackDeletions = true;
        FakeRouteInventory routeInv = new(throwOnSave: true);
        FakeEndpointInventory endpointInv = new();
        WindowsRuntimeExecutionStepHandler h = CreateHandler(routes, routeInv, endpointInv);

        RuntimeExecutionStepResult r = await h.ExecuteAndVerifyAsync(AddPrefixStep);

        Assert.Equal(RuntimeExecutionStepStatus.Failed, r.Status);
        Assert.Contains("removed as compensation", r.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, routes.AddCallCount);
        Assert.Equal(1, routes.DeleteCallCount);
        Assert.DoesNotContain(AddPrefixStep.Identity, routes.Present);
    }

    [Fact]
    public async Task AddPrefix_InventoryFailure_OrphanReported()
    {
        FakeRouteManager routes = new();
        routes.Present.Remove("203.0.113.0/24|192.168.1.1|10");
        FakeRouteInventory routeInv = new(throwOnSave: true);
        FakeEndpointInventory endpointInv = new();
        WindowsRuntimeExecutionStepHandler h = CreateHandler(routes, routeInv, endpointInv);

        RuntimeExecutionStepResult r = await h.ExecuteAndVerifyAsync(AddPrefixStep);

        Assert.Equal(RuntimeExecutionStepStatus.Failed, r.Status);
        Assert.Contains("Orphaned route", r.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(AddPrefixStep.Identity, routes.Present);
    }

    [Fact]
    public async Task AddEndpoint_PreexistingOwned_IdempotentSuccess()
    {
        FakeRouteManager routes = new();
        routes.Present.Clear();
        routes.Present.Add("10.0.0.1/32|192.168.1.1|10");
        FakeRouteInventory routeInv = new();
        FakeEndpointInventory endpointInv = new();
        endpointInv.SeedOwned(AddEndpointStep.Identity, addedByIranDirect: true);
        WindowsRuntimeExecutionStepHandler h = CreateHandler(routes, routeInv, endpointInv);

        RuntimeExecutionStepResult r = await h.ExecuteAndVerifyAsync(AddEndpointStep);

        Assert.Equal(RuntimeExecutionStepStatus.Succeeded, r.Status);
        Assert.Equal(0, routes.AddCallCount);
        Assert.Equal(0, routes.DeleteCallCount);
    }

    [Fact]
    public async Task AddEndpoint_PreexistingUnowned_ReturnsFailed()
    {
        FakeRouteManager routes = new();
        FakeRouteInventory routeInv = new();
        FakeEndpointInventory endpointInv = new();
        WindowsRuntimeExecutionStepHandler h = CreateHandler(routes, routeInv, endpointInv);

        RuntimeExecutionStepResult r = await h.ExecuteAndVerifyAsync(AddEndpointStep);

        Assert.Equal(RuntimeExecutionStepStatus.Failed, r.Status);
        Assert.Contains("not in the endpoint inventory", r.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, routes.AddCallCount);
    }

    [Fact]
    public async Task AddEndpoint_InventoryFailure_CompensatesSuccessfully()
    {
        FakeRouteManager routes = new();
        routes.Present.Remove("10.0.0.1/32|192.168.1.1|10");
        routes.TrackDeletions = true;
        FakeRouteInventory routeInv = new();
        FakeEndpointInventory endpointInv = new(throwOnSave: true);
        WindowsRuntimeExecutionStepHandler h = CreateHandler(routes, routeInv, endpointInv);

        RuntimeExecutionStepResult r = await h.ExecuteAndVerifyAsync(AddEndpointStep);

        Assert.Equal(RuntimeExecutionStepStatus.Failed, r.Status);
        Assert.Contains("removed as compensation", r.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, routes.AddCallCount);
        Assert.Equal(1, routes.DeleteCallCount);
        Assert.DoesNotContain(AddEndpointStep.Identity, routes.Present);
    }

    [Fact]
    public async Task AddEndpoint_InventoryFailure_OrphanReported()
    {
        FakeRouteManager routes = new();
        routes.Present.Remove("10.0.0.1/32|192.168.1.1|10");
        FakeRouteInventory routeInv = new();
        FakeEndpointInventory endpointInv = new(throwOnSave: true);
        WindowsRuntimeExecutionStepHandler h = CreateHandler(routes, routeInv, endpointInv);

        RuntimeExecutionStepResult r = await h.ExecuteAndVerifyAsync(AddEndpointStep);

        Assert.Equal(RuntimeExecutionStepStatus.Failed, r.Status);
        Assert.Contains("Orphaned route", r.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(AddEndpointStep.Identity, routes.Present);
    }

    [Fact]
    public async Task ConcurrentRemoval_DoesNotLoseUnrelatedEntries()
    {
        FakeRouteManager routes = new();
        routes.TrackDeletions = true;
        FakeRouteInventory routeInv = new();
        routeInv.Seed(AddPrefixStep.Identity);
        string otherIdentity = "198.51.100.0/24|192.168.1.1|10";
        routeInv.Seed(otherIdentity, additive: true);
        FakeEndpointInventory endpointInv = new();
        WindowsRuntimeExecutionStepHandler h = CreateHandler(routes, routeInv, endpointInv);

        RuntimeExecutionStepResult r = await h.ExecuteAndVerifyAsync(RemovePrefixStep);

        Assert.Equal(RuntimeExecutionStepStatus.Succeeded, r.Status);
        RouteInventory saved = await routeInv.LoadAsync();
        Assert.Single(saved.Routes);
        Assert.Equal(otherIdentity, saved.Routes[0].Identity);
    }

    private static WindowsRuntimeExecutionStepHandler CreateHandler(
        FakeRouteManager routes,
        FakeRouteInventory routeInv,
        FakeEndpointInventory endpointInv)
    {
        return new WindowsRuntimeExecutionStepHandler(routes, routeInv, endpointInv);
    }

    private sealed class FakeRouteManager : IRouteManager
    {
        private readonly bool _throwOnAdd;

        public HashSet<string> Present { get; } = new(StringComparer.OrdinalIgnoreCase)
        {
            "203.0.113.0/24|192.168.1.1|10",
            "10.0.0.1/32|192.168.1.1|10"
        };

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
                        RouteMetric = 256
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

            if (!SuppressRouteAdd)
            {
                foreach (ManagedRoute r in routes)
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
                    Present.Remove(r.Identity);
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
}
