using System.Net;
using PathVeer.Core.Routing;
using PathVeer.Core.SystemTools;
using PathVeer.Core.Testing.FaultInjection;

namespace PathVeer.Core.Tests.Routing;

public sealed class WindowsRouteManagerFaultInjectionTests
{
    [Fact]
    public async Task GetIpv4RoutesAsync_RouteEnumerationFault_ThrowsCorrectPoint_AndZeroNativeCalls()
    {
        FakeWindowsRouteApi api = new();
        WindowsRouteManager manager = CreateManager(
            api,
            FaultInjectionPolicy.For(
                [FaultInjectionPoint.RouteEnumeration]));

        FaultInjectionException exception =
            await Assert.ThrowsAsync<FaultInjectionException>(
                () => manager.GetIpv4RoutesAsync());

        Assert.Equal(
            FaultInjectionPoint.RouteEnumeration,
            exception.Point);
        Assert.Equal(0, api.EnumerateCallCount);
    }

    [Fact]
    public async Task GetIpv4RoutesAsync_NextUnfaultedEnumeration_Succeeds()
    {
        FakeWindowsRouteApi api = new();
        api.Present.Add("203.0.113.0/24|192.168.1.1|10");
        WindowsRouteManager manager = CreateManager(api);

        FaultInjectionException exception;
        using (FaultInjectionScope scope =
            FaultInjectionScope.Fail(
                FaultInjectionPoint.RouteEnumeration))
        {
            exception = await Assert.ThrowsAsync<FaultInjectionException>(
                () => manager.GetIpv4RoutesAsync());
        }

        Assert.Equal(
            FaultInjectionPoint.RouteEnumeration,
            exception.Point);
        Assert.Equal(0, api.EnumerateCallCount);

        IReadOnlyList<SystemRoute> routes =
            await manager.GetIpv4RoutesAsync();

        Assert.Single(routes);
        Assert.Equal(1, api.EnumerateCallCount);
    }

    [Fact]
    public async Task AddRoutesAsync_RouteCreateFault_ThrowsCorrectPoint_AndZeroNativeCalls()
    {
        FakeWindowsRouteApi api = new();
        WindowsRouteManager manager = CreateManager(
            api,
            FaultInjectionPolicy.For(
                [FaultInjectionPoint.RouteCreate]));

        FaultInjectionException exception =
            await Assert.ThrowsAsync<FaultInjectionException>(
                () => manager.AddRoutesAsync([CreateRoute()]));

        Assert.Equal(
            FaultInjectionPoint.RouteCreate,
            exception.Point);
        Assert.Equal(0, api.AddCallCount);
        Assert.Empty(api.Present);
    }

    [Fact]
    public async Task AddRoutesAsync_NextUnfaultedAdd_Succeeds()
    {
        FakeWindowsRouteApi api = new();
        WindowsRouteManager manager = CreateManager(api);
        ManagedRoute route = CreateRoute();

        FaultInjectionException exception;
        using (FaultInjectionScope scope =
            FaultInjectionScope.Fail(
                FaultInjectionPoint.RouteCreate))
        {
            exception = await Assert.ThrowsAsync<FaultInjectionException>(
                () => manager.AddRoutesAsync([route]));
        }

        Assert.Equal(
            FaultInjectionPoint.RouteCreate,
            exception.Point);
        Assert.Equal(0, api.AddCallCount);

        await manager.AddRoutesAsync([route]);

        Assert.Equal(1, api.AddCallCount);
        Assert.Contains(route.Identity, api.Present);
    }

    [Fact]
    public async Task DeleteRoutesAsync_RouteDeleteFault_ThrowsCorrectPoint_AndZeroNativeCalls()
    {
        FakeWindowsRouteApi api = new();
        api.Present.Add("203.0.113.0/24|192.168.1.1|10");
        WindowsRouteManager manager = CreateManager(
            api,
            FaultInjectionPolicy.For(
                [FaultInjectionPoint.RouteDelete]));

        FaultInjectionException exception =
            await Assert.ThrowsAsync<FaultInjectionException>(
                () => manager.DeleteRoutesAsync([CreateRoute()]));

        Assert.Equal(
            FaultInjectionPoint.RouteDelete,
            exception.Point);
        Assert.Equal(0, api.DeleteCallCount);
        Assert.Single(api.Present);
    }

    [Fact]
    public async Task DeleteRoutesAsync_NextUnfaultedDelete_Succeeds()
    {
        FakeWindowsRouteApi api = new();
        api.Present.Add("203.0.113.0/24|192.168.1.1|10");
        WindowsRouteManager manager = CreateManager(api);
        ManagedRoute route = CreateRoute();

        FaultInjectionException exception;
        using (FaultInjectionScope scope =
            FaultInjectionScope.Fail(
                FaultInjectionPoint.RouteDelete))
        {
            exception = await Assert.ThrowsAsync<FaultInjectionException>(
                () => manager.DeleteRoutesAsync([route]));
        }

        Assert.Equal(
            FaultInjectionPoint.RouteDelete,
            exception.Point);
        Assert.Equal(0, api.DeleteCallCount);

        await manager.DeleteRoutesAsync([route]);

        Assert.Equal(1, api.DeleteCallCount);
        Assert.Empty(api.Present);
    }

    [Fact]
    public async Task AddRoutesAsync_UnrelatedPoint_HasNoEffect()
    {
        FakeWindowsRouteApi api = new();
        WindowsRouteManager manager = CreateManager(
            api,
            FaultInjectionPolicy.For(
                [FaultInjectionPoint.HttpRequest]));

        await manager.AddRoutesAsync([CreateRoute()]);

        Assert.Equal(1, api.AddCallCount);
        Assert.Single(api.Present);
    }

    [Fact]
    public async Task GetIpv4RoutesAsync_InjectedPolicyWithoutAmbientScope_Throws()
    {
        FakeWindowsRouteApi api = new();
        WindowsRouteManager manager = CreateManager(
            api,
            FaultInjectionPolicy.For(
                [FaultInjectionPoint.RouteEnumeration]));

        Assert.False(FaultInjectionScope.IsActive);

        FaultInjectionException exception =
            await Assert.ThrowsAsync<FaultInjectionException>(
                () => manager.GetIpv4RoutesAsync());

        Assert.Equal(
            FaultInjectionPoint.RouteEnumeration,
            exception.Point);
    }

    [Fact]
    public async Task GetIpv4RoutesAsync_AmbientScope_OverridesInjectedPolicy()
    {
        FakeWindowsRouteApi neverApi = new();
        WindowsRouteManager injectedNever = CreateManager(
            neverApi,
            FaultInjectionPolicy.Never);

        FaultInjectionException exception;
        using (FaultInjectionScope scope =
            FaultInjectionScope.Fail(
                FaultInjectionPoint.RouteEnumeration))
        {
            exception = await Assert.ThrowsAsync<FaultInjectionException>(
                () => injectedNever.GetIpv4RoutesAsync());
        }

        Assert.Equal(
            FaultInjectionPoint.RouteEnumeration,
            exception.Point);

        FakeWindowsRouteApi enumerationApi = new();
        enumerationApi.Present.Add("203.0.113.0/24|192.168.1.1|10");
        WindowsRouteManager injectedEnumeration = CreateManager(
            enumerationApi,
            FaultInjectionPolicy.For(
                [FaultInjectionPoint.RouteEnumeration]));

        using (FaultInjectionScope scope =
            FaultInjectionScope.Fail(
                FaultInjectionPoint.RouteCreate))
        {
            IReadOnlyList<SystemRoute> routes =
                await injectedEnumeration.GetIpv4RoutesAsync();

            Assert.Single(routes);
        }
    }

    [Fact]
    public async Task GetIpv4RoutesAsync_NestedScopes_InnerSelectsRoutePoint()
    {
        FakeWindowsRouteApi api = new();
        api.Present.Add("203.0.113.0/24|192.168.1.1|10");
        WindowsRouteManager manager = CreateManager(api);

        FaultInjectionException innerException;
        using (FaultInjectionScope outer =
            FaultInjectionScope.Fail(
                FaultInjectionPoint.RouteCreate))
        {
            using (FaultInjectionScope inner =
                FaultInjectionScope.Fail(
                    FaultInjectionPoint.RouteEnumeration))
            {
                innerException =
                    await Assert.ThrowsAsync<FaultInjectionException>(
                        () => manager.GetIpv4RoutesAsync());
            }

            Assert.Equal(
                FaultInjectionPoint.RouteEnumeration,
                innerException.Point);
            Assert.Equal(0, api.EnumerateCallCount);

            IReadOnlyList<SystemRoute> routes =
                await manager.GetIpv4RoutesAsync();

            Assert.Single(routes);
            Assert.Equal(1, api.EnumerateCallCount);

            FaultInjectionException outerException =
                await Assert.ThrowsAsync<FaultInjectionException>(
                    () => manager.AddRoutesAsync([CreateRoute()]));

            Assert.Equal(
                FaultInjectionPoint.RouteCreate,
                outerException.Point);
        }
    }

    [Fact]
    public async Task AddRoutesAsync_AfterScopeDisposal_NoLeakage()
    {
        FakeWindowsRouteApi api = new();
        WindowsRouteManager manager = CreateManager(api);
        ManagedRoute route = CreateRoute();

        using (FaultInjectionScope scope =
            FaultInjectionScope.Fail(
                FaultInjectionPoint.RouteCreate))
        {
            await Assert.ThrowsAsync<FaultInjectionException>(
                () => manager.AddRoutesAsync([route]));
        }

        Assert.False(FaultInjectionScope.IsActive);

        await manager.AddRoutesAsync([route]);

        Assert.Equal(1, api.AddCallCount);
        Assert.Contains(route.Identity, api.Present);
    }

    [Fact]
    public async Task AddRoutesAsync_BatchFault_IsNotPartiallySubmitted()
    {
        FakeWindowsRouteApi api = new();
        WindowsRouteManager manager = CreateManager(
            api,
            FaultInjectionPolicy.For(
                [FaultInjectionPoint.RouteCreate]));

        ManagedRoute[] batch =
        [
            CreateRoute("10.0.0.0/24", "192.168.1.1", 10),
            CreateRoute("10.0.1.0/24", "192.168.1.1", 10),
            CreateRoute("10.0.2.0/24", "192.168.1.1", 10)
        ];

        await Assert.ThrowsAsync<FaultInjectionException>(
            () => manager.AddRoutesAsync(batch));

        Assert.Equal(0, api.AddCallCount);
        Assert.Empty(api.Present);
    }

    [Fact]
    public async Task AddRoutesAsync_EmptyBatch_WithFaultActive_IsNoOp()
    {
        FakeWindowsRouteApi api = new();
        WindowsRouteManager manager = CreateManager(
            api,
            FaultInjectionPolicy.For(
                [FaultInjectionPoint.RouteCreate]));

        using (FaultInjectionScope scope =
            FaultInjectionScope.Fail(
                FaultInjectionPoint.RouteCreate))
        {
            await manager.AddRoutesAsync([]);
        }

        Assert.Equal(0, api.AddCallCount);
    }

    [Fact]
    public async Task GetIpv4RoutesAsync_NoFault_DelegatesToApi()
    {
        FakeWindowsRouteApi api = new();
        api.Present.Add("203.0.113.0/24|192.168.1.1|10");
        WindowsRouteManager manager = CreateManager(api);

        IReadOnlyList<SystemRoute> routes =
            await manager.GetIpv4RoutesAsync();

        Assert.Single(routes);
        Assert.Equal(1, api.EnumerateCallCount);
    }

    [Fact]
    public async Task AddRoutesAsync_NoFault_DelegatesToApi()
    {
        FakeWindowsRouteApi api = new();
        WindowsRouteManager manager = CreateManager(api);

        await manager.AddRoutesAsync([CreateRoute()]);

        Assert.Equal(1, api.AddCallCount);
        Assert.Single(api.Present);
    }

    private static WindowsRouteManager CreateManager(
        IWindowsRouteApi routeApi,
        IFaultInjectionPolicy? faultPolicy = null) =>
        new(new CommandRunner(), routeApi, faultPolicy);

    private static ManagedRoute CreateRoute(
        string prefix = "203.0.113.0/24",
        string gateway = "192.168.1.1",
        uint interfaceIndex = 10) =>
        new()
        {
            DestinationPrefix = prefix,
            Gateway = IPAddress.Parse(gateway),
            InterfaceIndex = interfaceIndex,
            Metric = 256
        };

    private sealed class FakeWindowsRouteApi : IWindowsRouteApi
    {
        public HashSet<string> Present { get; } =
            new(StringComparer.OrdinalIgnoreCase);

        public int EnumerateCallCount { get; private set; }

        public int AddCallCount { get; private set; }

        public int DeleteCallCount { get; private set; }

        public Task<IReadOnlyList<SystemRoute>> EnumerateAsync(
            CancellationToken cancellationToken = default)
        {
            EnumerateCallCount++;
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult<IReadOnlyList<SystemRoute>>(
                Present
                    .Select(ToSystemRoute)
                    .ToArray());
        }

        public Task AddAsync(
            IReadOnlyCollection<ManagedRoute> routes,
            CancellationToken cancellationToken = default)
        {
            AddCallCount++;
            cancellationToken.ThrowIfCancellationRequested();

            foreach (ManagedRoute route in routes)
            {
                Present.Add(route.Identity);
            }

            return Task.CompletedTask;
        }

        public Task DeleteAsync(
            IReadOnlyCollection<ManagedRoute> routes,
            CancellationToken cancellationToken = default)
        {
            DeleteCallCount++;
            cancellationToken.ThrowIfCancellationRequested();

            foreach (ManagedRoute route in routes)
            {
                Present.Remove(route.Identity);
            }

            return Task.CompletedTask;
        }

        private static SystemRoute ToSystemRoute(string identity)
        {
            string[] parts = identity.Split('|');

            return new SystemRoute
            {
                DestinationPrefix = parts[0],
                NextHop = IPAddress.Parse(parts[1]),
                InterfaceIndex = uint.Parse(parts[2]),
                RouteMetric = 256
            };
        }
    }
}
