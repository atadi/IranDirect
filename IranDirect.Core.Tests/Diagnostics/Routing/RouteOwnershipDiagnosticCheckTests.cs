using System.Net;
using IranDirect.Core.Diagnostics;
using IranDirect.Core.Diagnostics.Routing;
using IranDirect.Core.Observability;
using IranDirect.Core.Routing;

namespace IranDirect.Core.Tests.Diagnostics.Routing;

public sealed class RouteOwnershipDiagnosticCheckTests
{
    private sealed class FakeRouteManager : IRouteManager
    {
        private readonly List<SystemRoute> _routes;
        private readonly Exception? _exception;

        public FakeRouteManager(
            IEnumerable<SystemRoute>? routes = null,
            Exception? exception = null)
        {
            _routes = routes?.ToList() ?? [];
            _exception = exception;
        }

        public Task<IReadOnlyList<SystemRoute>>
            GetIpv4RoutesAsync(
                CancellationToken cancellationToken =
                    default)
        {
            if (_exception is not null)
            {
                throw _exception;
            }

            return Task.FromResult<IReadOnlyList<SystemRoute>>(
                _routes.ToArray());
        }

        public Task AddRoutesAsync(
            IReadOnlyCollection<ManagedRoute> routes,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task DeleteRoutesAsync(
            IReadOnlyCollection<ManagedRoute> routes,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class FakeInventory :
        IRouteInventoryPersistence
    {
        private readonly RouteInventory _inventory;
        private readonly Exception? _exception;

        public FakeInventory(
            RouteInventory inventory,
            Exception? exception = null)
        {
            _inventory = inventory;
            _exception = exception;
        }

        public Task<RouteInventory> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            if (_exception is not null)
            {
                throw _exception;
            }

            return Task.FromResult(_inventory);
        }

        public Task SaveAsync(
            RouteInventory inventory,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task MutateAsync(
            Func<RouteInventory, RouteInventory> transform,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private static SystemRoute SysRoute(
        string prefix = "10.0.0.0/8",
        string gateway = "192.168.1.1",
        uint iface = 1)
    {
        return new SystemRoute
        {
            DestinationPrefix = prefix,
            NextHop = IPAddress.Parse(gateway),
            InterfaceIndex = iface,
            RouteMetric = 5
        };
    }

    private static RouteInventoryItem InvItem(
        string prefix = "10.0.0.0/8",
        string gateway = "192.168.1.1",
        uint iface = 1)
    {
        return new RouteInventoryItem
        {
            DestinationPrefix = prefix,
            Gateway = gateway,
            InterfaceIndex = iface,
            Metric = 5
        };
    }

    [Fact]
    public async Task CheckAsync_AllMatch_ReturnsPassed()
    {
        var manager = new FakeRouteManager(
            new[] { SysRoute() });
        var inventory = new FakeInventory(
            new RouteInventory { Routes = [InvItem()] });

        var check = new RouteOwnershipDiagnosticCheck(
            manager, inventory);

        DiagnosticResult result =
            await check.CheckAsync(CancellationToken.None);

        Assert.Equal(DiagnosticStatus.Passed, result.Status);
    }

    [Fact]
    public async Task CheckAsync_MissingFromWindows_ReturnsWarning()
    {
        var manager = new FakeRouteManager(
            new SystemRoute[] { });
        var inventory = new FakeInventory(
            new RouteInventory { Routes = [InvItem()] });

        var check = new RouteOwnershipDiagnosticCheck(
            manager, inventory);

        DiagnosticResult result =
            await check.CheckAsync(CancellationToken.None);

        Assert.Equal(DiagnosticStatus.Warning, result.Status);
        Assert.Contains(
            "missing from Windows", result.Message);
    }

    [Fact]
    public async Task CheckAsync_DuplicateOwnership_ReturnsWarning()
    {
        var manager = new FakeRouteManager(
            new[] { SysRoute() });
        var inventory = new FakeInventory(
            new RouteInventory
            {
                Routes = [InvItem(), InvItem()]
            });

        var check = new RouteOwnershipDiagnosticCheck(
            manager, inventory);

        DiagnosticResult result =
            await check.CheckAsync(CancellationToken.None);

        Assert.Equal(DiagnosticStatus.Warning, result.Status);
        Assert.Contains(
            "Duplicate inventory", result.Message);
    }

    [Fact]
    public async Task CheckAsync_InventoryReadError_ReturnsFailed()
    {
        var manager = new FakeRouteManager();
        var inventory = new FakeInventory(
            new RouteInventory(),
            exception: new IOException("not found"));

        var check = new RouteOwnershipDiagnosticCheck(
            manager, inventory);

        DiagnosticResult result =
            await check.CheckAsync(CancellationToken.None);

        Assert.Equal(DiagnosticStatus.Failed, result.Status);
        Assert.Equal(
            DiagnosticSeverity.Error, result.Severity);
    }

    [Fact]
    public async Task CheckAsync_WindowsReadError_ReturnsFailed()
    {
        var manager = new FakeRouteManager(
            exception: new IOException("access denied"));
        var inventory = new FakeInventory(
            new RouteInventory { Routes = [InvItem()] });

        var check = new RouteOwnershipDiagnosticCheck(
            manager, inventory);

        DiagnosticResult result =
            await check.CheckAsync(CancellationToken.None);

        Assert.Equal(DiagnosticStatus.Failed, result.Status);
        Assert.Equal(
            DiagnosticSeverity.Error, result.Severity);
    }

    [Fact]
    public async Task CheckAsync_EmptyInventory_ReturnsPassed()
    {
        var manager = new FakeRouteManager(
            new[] { SysRoute() });
        var inventory = new FakeInventory(
            new RouteInventory { Routes = [] });

        var check = new RouteOwnershipDiagnosticCheck(
            manager, inventory);

        DiagnosticResult result =
            await check.CheckAsync(CancellationToken.None);

        Assert.Equal(DiagnosticStatus.Passed, result.Status);
        Assert.Contains("0 inventory", result.Message);
    }

    [Fact]
    public void CheckAsync_HasCorrectIdAndTitle()
    {
        var check = new RouteOwnershipDiagnosticCheck(
            new FakeRouteManager(),
            new FakeInventory(new RouteInventory()));

        Assert.Equal("route-ownership", check.Id);
        Assert.Equal("Route ownership", check.Title);
    }
}
