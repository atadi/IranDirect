using System.Net;
using PathVeer.Core.Routing;
using PathVeer.Core.Tests.TestDoubles;

namespace PathVeer.Core.Tests.Routing;

public sealed class RouteReconcilerTests
{
    [Fact]
    public async Task EnableAsync_AddsOnlyMissingRoutes()
    {
        IPAddress gateway = IPAddress.Parse("192.168.100.1");

        FakeRouteManager routeManager = new(
        [
            new SystemRoute
            {
                DestinationPrefix = "10.0.0.0/24",
                NextHop = gateway,
                InterfaceIndex = 30,
                RouteMetric = 5
            }
        ]);

        RouteReconciler reconciler = new(routeManager);

        ReconciliationResult result =
            await reconciler.EnableAsync(
                ["10.0.0.0/24", "10.0.1.0/24"],
                gateway,
                30);

        Assert.Equal(2, result.DesiredCount);
        Assert.Equal(1, result.ExistingCount);
        Assert.Equal(1, result.AddedCount);

        ManagedRoute added =
            Assert.Single(routeManager.AddedRoutes);

        Assert.Equal("10.0.1.0/24",
            added.DestinationPrefix);
    }

    [Fact]
    public async Task DisableAsync_RemovesOnlyMatchingOwnedRoutes()
    {
        IPAddress gateway = IPAddress.Parse("192.168.100.1");

        FakeRouteManager routeManager = new(
        [
            new SystemRoute
            {
                DestinationPrefix = "10.0.0.0/24",
                NextHop = gateway,
                InterfaceIndex = 30,
                RouteMetric = 5
            },
            new SystemRoute
            {
                DestinationPrefix = "10.0.1.0/24",
                NextHop = IPAddress.Parse("192.168.100.254"),
                InterfaceIndex = 30,
                RouteMetric = 5
            }
        ]);

        RouteReconciler reconciler = new(routeManager);

        ReconciliationResult result =
            await reconciler.DisableAsync(
            [
                new ManagedRoute
                {
                    DestinationPrefix = "10.0.0.0/24",
                    Gateway = gateway,
                    InterfaceIndex = 30
                },
                new ManagedRoute
                {
                    DestinationPrefix = "10.0.1.0/24",
                    Gateway = gateway,
                    InterfaceIndex = 30
                }
            ]);

        Assert.Equal(1, result.RemovedCount);

        ManagedRoute deleted =
            Assert.Single(routeManager.DeletedRoutes);

        Assert.Equal("10.0.0.0/24",
            deleted.DestinationPrefix);
    }
}