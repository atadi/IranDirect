using System.Net;
using IranDirect.Core.Diagnostics;
using IranDirect.Core.Diagnostics.Routing;
using IranDirect.Core.Observability;
using IranDirect.Core.Routing;

namespace IranDirect.Core.Tests.Diagnostics.Routing;

public sealed class ManagedRouteConsistencyDiagnosticCheckTests
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

    private sealed class FakeSnapshotProvider :
        IRuntimeSnapshotProvider
    {
        private readonly RuntimeSnapshot _snapshot;
        private readonly Exception? _exception;

        public FakeSnapshotProvider(
            RuntimeSnapshot snapshot,
            Exception? exception = null)
        {
            _snapshot = snapshot;
            _exception = exception;
        }

        public Task<RuntimeSnapshot> GetSnapshotAsync(
            CancellationToken cancellationToken = default)
        {
            if (_exception is not null)
            {
                throw _exception;
            }

            return Task.FromResult(_snapshot);
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

    private static RuntimeSnapshot Snapshot(
        int managedCount = 0) =>
        new()
        {
            CapturedAt = DateTimeOffset.UtcNow,
            SchemaVersion = 1,
            InstalledRouteCount = managedCount,
            DnsCache = []
        };

    [Fact]
    public async Task CheckAsync_AllAligned_ReturnsPassed()
    {
        var manager = new FakeRouteManager(
            new[] { SysRoute() });
        var inventory = new FakeInventory(
            new RouteInventory { Routes = [InvItem()] });
        var snapshot = new FakeSnapshotProvider(
            Snapshot(managedCount: 1));

        var check = new ManagedRouteConsistencyDiagnosticCheck(
            manager, inventory, snapshot);

        DiagnosticResult result =
            await check.CheckAsync(CancellationToken.None);

        Assert.Equal(DiagnosticStatus.Passed, result.Status);
        Assert.Contains("Managed: 1", result.Message);
        Assert.Contains("Inventory: 1", result.Message);
        Assert.Contains("Windows: 1", result.Message);
    }

    [Fact]
    public async Task CheckAsync_DuplicateInventory_ReturnsWarning()
    {
        var manager = new FakeRouteManager(
            new[] { SysRoute() });
        var inventory = new FakeInventory(
            new RouteInventory
            {
                Routes = [InvItem(), InvItem()]
            });
        var snapshot = new FakeSnapshotProvider(Snapshot());

        var check = new ManagedRouteConsistencyDiagnosticCheck(
            manager, inventory, snapshot);

        DiagnosticResult result =
            await check.CheckAsync(CancellationToken.None);

        Assert.Equal(DiagnosticStatus.Warning, result.Status);
        Assert.Contains("Duplicate inventory", result.Message);
    }

    [Fact]
    public async Task CheckAsync_GatewayMismatch_ReturnsWarning()
    {
        var manager = new FakeRouteManager(
            new[] { SysRoute(gateway: "10.0.0.1") });
        var inventory = new FakeInventory(
            new RouteInventory
            {
                Routes = [InvItem(gateway: "192.168.1.1")]
            });
        var snapshot = new FakeSnapshotProvider(Snapshot());

        var check = new ManagedRouteConsistencyDiagnosticCheck(
            manager, inventory, snapshot);

        DiagnosticResult result =
            await check.CheckAsync(CancellationToken.None);

        Assert.Equal(DiagnosticStatus.Warning, result.Status);
        Assert.Contains("Gateway mismatches", result.Message);
    }

    [Fact]
    public async Task CheckAsync_InterfaceMismatch_ReturnsWarning()
    {
        var manager = new FakeRouteManager(
            new[] { SysRoute(iface: 2) });
        var inventory = new FakeInventory(
            new RouteInventory
            {
                Routes = [InvItem(iface: 1)]
            });
        var snapshot = new FakeSnapshotProvider(Snapshot());

        var check = new ManagedRouteConsistencyDiagnosticCheck(
            manager, inventory, snapshot);

        DiagnosticResult result =
            await check.CheckAsync(CancellationToken.None);

        Assert.Equal(DiagnosticStatus.Warning, result.Status);
        Assert.Contains(
            "Interface mismatches", result.Message);
    }

    [Fact]
    public async Task CheckAsync_SnapshotReadError_ReturnsFailed()
    {
        var manager = new FakeRouteManager();
        var inventory = new FakeInventory(
            new RouteInventory());
        var snapshot = new FakeSnapshotProvider(
            Snapshot(),
            exception: new InvalidOperationException("no"));

        var check = new ManagedRouteConsistencyDiagnosticCheck(
            manager, inventory, snapshot);

        DiagnosticResult result =
            await check.CheckAsync(CancellationToken.None);

        Assert.Equal(DiagnosticStatus.Failed, result.Status);
        Assert.Equal(
            DiagnosticSeverity.Error, result.Severity);
    }

    [Fact]
    public async Task CheckAsync_InventoryReadError_ReturnsFailed()
    {
        var manager = new FakeRouteManager();
        var inventory = new FakeInventory(
            new RouteInventory(),
            exception: new IOException("not found"));
        var snapshot = new FakeSnapshotProvider(Snapshot());

        var check = new ManagedRouteConsistencyDiagnosticCheck(
            manager, inventory, snapshot);

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
            exception: new IOException("denied"));
        var inventory = new FakeInventory(
            new RouteInventory());
        var snapshot = new FakeSnapshotProvider(Snapshot());

        var check = new ManagedRouteConsistencyDiagnosticCheck(
            manager, inventory, snapshot);

        DiagnosticResult result =
            await check.CheckAsync(CancellationToken.None);

        Assert.Equal(DiagnosticStatus.Failed, result.Status);
        Assert.Equal(
            DiagnosticSeverity.Error, result.Severity);
    }

    [Fact]
    public async Task CheckAsync_EmptySources_ReturnsPassed()
    {
        var manager = new FakeRouteManager();
        var inventory = new FakeInventory(
            new RouteInventory { Routes = [] });
        var snapshot = new FakeSnapshotProvider(Snapshot());

        var check = new ManagedRouteConsistencyDiagnosticCheck(
            manager, inventory, snapshot);

        DiagnosticResult result =
            await check.CheckAsync(CancellationToken.None);

        Assert.Equal(DiagnosticStatus.Passed, result.Status);
        Assert.Contains("Managed: 0", result.Message);
        Assert.Contains("Inventory: 0", result.Message);
        Assert.Contains("Windows: 0", result.Message);
    }

    [Fact]
    public async Task CheckAsync_MismatchedPrefix_ExtraInInventory()
    {
        var manager = new FakeRouteManager(
            new[] { SysRoute(prefix: "10.0.0.0/8") });
        var inventory = new FakeInventory(
            new RouteInventory
            {
                Routes = [InvItem(prefix: "172.16.0.0/12")]
            });
        var snapshot = new FakeSnapshotProvider(Snapshot());

        var check = new ManagedRouteConsistencyDiagnosticCheck(
            manager, inventory, snapshot);

        DiagnosticResult result =
            await check.CheckAsync(CancellationToken.None);

        Assert.Equal(DiagnosticStatus.Passed, result.Status);
        Assert.Contains("Extra in inventory: 1", result.Message);
    }

    [Fact]
    public void CheckAsync_HasCorrectIdAndTitle()
    {
        var check = new ManagedRouteConsistencyDiagnosticCheck(
            new FakeRouteManager(),
            new FakeInventory(new RouteInventory()),
            new FakeSnapshotProvider(Snapshot()));

        Assert.Equal(
            "managed-route-consistency", check.Id);
        Assert.Equal(
            "Managed route consistency", check.Title);
    }
}
