using IranDirect.Core.Routing;
using IranDirect.Core.Runtime.Reconciliation;
using IranDirect.Core.Vpn;

namespace IranDirect.Core.Tests.Runtime.Reconciliation;

public sealed class InventoryRouteOwnershipSourceTests
{
    [Fact]
    public async Task
        LoadPrefixRouteIdentitiesAsync_WhenInventoryHasRoutes_ReturnsAllIdentities()
    {
        string dir = CreateTemporaryDirectory();
        RouteInventoryStore routeStore = new(
            Path.Combine(dir, "route-inventory.json"));

        await routeStore.SaveAsync(
            new RouteInventory
            {
                Routes =
                [
                    new RouteInventoryItem
                    {
                        DestinationPrefix = "203.0.113.0/24",
                        Gateway = "192.168.100.1",
                        InterfaceIndex = 30
                    },
                    new RouteInventoryItem
                    {
                        DestinationPrefix = "198.51.100.0/24",
                        Gateway = "10.0.0.1",
                        InterfaceIndex = 20
                    }
                ]
            });

        VpnEndpointInventoryStore endpointStore = new(
            Path.Combine(dir, "endpoint-inventory.json"));

        InventoryRouteOwnershipSource source = new(
            routeStore, endpointStore);

        IReadOnlyCollection<string> identities =
            await source.LoadPrefixRouteIdentitiesAsync();

        Assert.Equal(2, identities.Count);
        Assert.Contains(
            "203.0.113.0/24|192.168.100.1|30",
            identities);
        Assert.Contains(
            "198.51.100.0/24|10.0.0.1|20",
            identities);
    }

    [Fact]
    public async Task
        LoadPrefixRouteIdentitiesAsync_WhenInventoryIsEmpty_ReturnsEmpty()
    {
        string dir = CreateTemporaryDirectory();
        RouteInventoryStore routeStore = new(
            Path.Combine(dir, "route-inventory.json"));
        VpnEndpointInventoryStore endpointStore = new(
            Path.Combine(dir, "endpoint-inventory.json"));
        InventoryRouteOwnershipSource source = new(
            routeStore, endpointStore);

        IReadOnlyCollection<string> identities =
            await source.LoadPrefixRouteIdentitiesAsync();

        Assert.Empty(identities);
    }

    [Fact]
    public async Task
        LoadEndpointRouteIdentitiesAsync_ReturnsOnlyAddedByIranDirect()
    {
        string dir = CreateTemporaryDirectory();
        VpnEndpointInventoryStore endpointStore = new(
            Path.Combine(dir, "endpoint-inventory.json"));

        await endpointStore.SaveAsync(
            new VpnEndpointInventory
            {
                Endpoints =
                [
                    new VpnEndpointInventoryItem
                    {
                        Host = "vpn1.example.com",
                        Address = "5.160.74.148",
                        Port = 1409,
                        Protocol = "tcp",
                        DestinationPrefix = "5.160.74.148/32",
                        Gateway = "192.168.100.1",
                        InterfaceIndex = 30,
                        AddedByIranDirect = true
                    },
                    new VpnEndpointInventoryItem
                    {
                        Host = "vpn2.example.com",
                        Address = "10.0.0.1",
                        Port = 1409,
                        Protocol = "udp",
                        DestinationPrefix = "10.0.0.1/32",
                        Gateway = "192.168.100.1",
                        InterfaceIndex = 30,
                        AddedByIranDirect = false
                    }
                ]
            });

        RouteInventoryStore routeStore = new(
            Path.Combine(dir, "route-inventory.json"));
        InventoryRouteOwnershipSource source = new(
            routeStore, endpointStore);

        IReadOnlyCollection<string> identities =
            await source.LoadEndpointRouteIdentitiesAsync();

        Assert.Single(identities);
        Assert.Contains(
            "5.160.74.148/32|192.168.100.1|30",
            identities);
    }

    [Fact]
    public async Task
        LoadEndpointRouteIdentitiesAsync_WhenInventoryIsEmpty_ReturnsEmpty()
    {
        string dir = CreateTemporaryDirectory();
        VpnEndpointInventoryStore endpointStore = new(
            Path.Combine(dir, "endpoint-inventory.json"));
        RouteInventoryStore routeStore = new(
            Path.Combine(dir, "route-inventory.json"));
        InventoryRouteOwnershipSource source = new(
            routeStore, endpointStore);

        IReadOnlyCollection<string> identities =
            await source.LoadEndpointRouteIdentitiesAsync();

        Assert.Empty(identities);
    }

    [Fact]
    public async Task
        LoadEndpointRouteIdentitiesAsync_WhenNoneAddedByIranDirect_ReturnsEmpty()
    {
        string dir = CreateTemporaryDirectory();
        VpnEndpointInventoryStore endpointStore = new(
            Path.Combine(dir, "endpoint-inventory.json"));

        await endpointStore.SaveAsync(
            new VpnEndpointInventory
            {
                Endpoints =
                [
                    new VpnEndpointInventoryItem
                    {
                        Host = "vpn.example.com",
                        Address = "5.160.74.148",
                        Port = 1409,
                        Protocol = "tcp",
                        DestinationPrefix = "5.160.74.148/32",
                        Gateway = "192.168.100.1",
                        InterfaceIndex = 30,
                        AddedByIranDirect = false
                    }
                ]
            });

        RouteInventoryStore routeStore = new(
            Path.Combine(dir, "route-inventory.json"));
        InventoryRouteOwnershipSource source = new(
            routeStore, endpointStore);

        IReadOnlyCollection<string> identities =
            await source.LoadEndpointRouteIdentitiesAsync();

        Assert.Empty(identities);
    }

    [Fact]
    public async Task
        OwnershipCategories_AreIndependent()
    {
        string dir = CreateTemporaryDirectory();
        RouteInventoryStore routeStore = new(
            Path.Combine(dir, "route-inventory.json"));

        await routeStore.SaveAsync(
            new RouteInventory
            {
                Routes =
                [
                    new RouteInventoryItem
                    {
                        DestinationPrefix = "5.160.74.148/32",
                        Gateway = "192.168.100.1",
                        InterfaceIndex = 30
                    }
                ]
            });

        VpnEndpointInventoryStore endpointStore = new(
            Path.Combine(dir, "endpoint-inventory.json"));

        await endpointStore.SaveAsync(
            new VpnEndpointInventory
            {
                Endpoints =
                [
                    new VpnEndpointInventoryItem
                    {
                        Host = "vpn.example.com",
                        Address = "5.160.74.148",
                        Port = 1409,
                        Protocol = "tcp",
                        DestinationPrefix = "5.160.74.148/32",
                        Gateway = "192.168.100.1",
                        InterfaceIndex = 30,
                        AddedByIranDirect = true
                    }
                ]
            });

        InventoryRouteOwnershipSource source = new(
            routeStore, endpointStore);

        IReadOnlyCollection<string> prefixIdentities =
            await source.LoadPrefixRouteIdentitiesAsync();
        IReadOnlyCollection<string> endpointIdentities =
            await source.LoadEndpointRouteIdentitiesAsync();

        const string sameIdentity =
            "5.160.74.148/32|192.168.100.1|30";

        Assert.Contains(sameIdentity, prefixIdentities);
        Assert.Contains(sameIdentity, endpointIdentities);
    }

    private static string CreateTemporaryDirectory()
    {
        string dir = Path.Combine(
            Path.GetTempPath(),
            "IranDirect.Tests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(dir);
        return dir;
    }
}
