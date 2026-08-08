using System.Net;
using PathVeer.Core.Models;
using PathVeer.Core.Routing;
using PathVeer.Core.Tests.TestDoubles;
using PathVeer.Core.Vpn;

namespace PathVeer.Core.Tests.Vpn;

public sealed class VpnEndpointRouteManagerTests
{
    private static readonly DirectGateway Gateway = new()
    {
        Address = IPAddress.Parse("192.168.100.1"),
        InterfaceIndex = 30,
        InterfaceName = "Ethernet",
        InterfaceMetric = 10
    };

    [Fact]
    public async Task EnsureProtectedAsync_AddsAndVerifiesEndpointRoute()
    {
        FakeRouteManager routeManager = new();
        VpnEndpointRouteManager manager = new(routeManager);

        VpnEndpointProtectionResult result =
            await manager.EnsureProtectedAsync(
            [
                new ResolvedVpnEndpoint
                {
                    Host = "vpn.example",
                    Address = "5.160.74.148",
                    Port = 1409,
                    Protocol = "tcp"
                }
            ],
            Gateway);

        ManagedRoute added =
            Assert.Single(routeManager.AddedRoutes);

        Assert.Equal("5.160.74.148/32",
            added.DestinationPrefix);
        Assert.Equal(1, added.Metric);
        Assert.Contains(
            added.Identity,
            result.AddedRouteIdentities);
    }

    [Fact]
    public async Task EnsureProtectedAsync_DoesNotClaimExistingRoute()
    {
        FakeRouteManager routeManager = new(
        [
            new SystemRoute
            {
                DestinationPrefix = "5.160.74.148/32",
                NextHop = Gateway.Address,
                InterfaceIndex = Gateway.InterfaceIndex,
                RouteMetric = 1
            }
        ]);

        VpnEndpointRouteManager manager = new(routeManager);

        VpnEndpointProtectionResult result =
            await manager.EnsureProtectedAsync(
            [
                new ResolvedVpnEndpoint
                {
                    Host = "5.160.74.148",
                    Address = "5.160.74.148",
                    Port = 1409,
                    Protocol = "tcp"
                }
            ],
            Gateway);

        Assert.Empty(routeManager.AddedRoutes);
        Assert.Empty(result.AddedRouteIdentities);
        Assert.Single(result.ProtectedRoutes);
    }

    [Fact]
    public async Task GetHealthAsync_ReportsProtectedCurrentEndpointsOnly()
    {
        FakeRouteManager routeManager = new(
        [
            new SystemRoute
            {
                DestinationPrefix = "5.160.74.148/32",
                NextHop = Gateway.Address,
                InterfaceIndex = Gateway.InterfaceIndex,
                RouteMetric = 1
            }
        ]);

        VpnEndpointRouteManager manager = new(routeManager);

        VpnEndpointProtectionHealth health =
            await manager.GetHealthAsync(
            [
                new VpnEndpointInventoryItem
                {
                    Host = "vpn.example",
                    Address = "5.160.74.148",
                    Port = 1409,
                    Protocol = "tcp",
                    DestinationPrefix = "5.160.74.148/32",
                    Gateway = "192.168.100.1",
                    InterfaceIndex = 30,
                    IsCurrent = true,
                    ProtectedAt = DateTimeOffset.UtcNow,
                    LastSeenAt = DateTimeOffset.UtcNow
                },
                new VpnEndpointInventoryItem
                {
                    Host = "old.example",
                    Address = "188.132.183.218",
                    Port = 1194,
                    Protocol = "udp",
                    DestinationPrefix = "188.132.183.218/32",
                    Gateway = "192.168.100.1",
                    InterfaceIndex = 30,
                    IsCurrent = false,
                    ProtectedAt = DateTimeOffset.UtcNow,
                    LastSeenAt = DateTimeOffset.UtcNow
                }
            ]);

        Assert.True(health.IsProtected);
        Assert.Equal(1, health.CurrentEndpointCount);
        Assert.Equal(1, health.ProtectedEndpointCount);
    }
}