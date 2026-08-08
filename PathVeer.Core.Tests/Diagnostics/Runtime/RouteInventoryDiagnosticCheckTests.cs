using PathVeer.Core.Diagnostics;
using PathVeer.Core.Diagnostics.Runtime;
using PathVeer.Core.Routing;

namespace PathVeer.Core.Tests.Diagnostics.Runtime;

public sealed class RouteInventoryDiagnosticCheckTests
{
    private sealed class FakeRouteInventoryPersistence :
        IRouteInventoryPersistence
    {
        private readonly RouteInventory _inventory = null!;
        private readonly Exception? _exception;

        public FakeRouteInventoryPersistence(
            RouteInventory inventory)
        {
            _inventory = inventory;
        }

        public FakeRouteInventoryPersistence(Exception exception)
        {
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

    private static RouteInventoryItem ValidItem(
        string prefix = "10.0.0.0",
        string gateway = "192.168.1.1",
        uint interfaceIndex = 1)
    {
        return new RouteInventoryItem
        {
            DestinationPrefix = prefix,
            Gateway = gateway,
            InterfaceIndex = interfaceIndex
        };
    }

    [Fact]
    public async Task CheckAsync_ValidInventory_ReturnsPassed()
    {
        var inventory = new RouteInventory
        {
            SchemaVersion = 1,
            Routes = [ValidItem()]
        };

        var check = new RouteInventoryDiagnosticCheck(
            new FakeRouteInventoryPersistence(inventory));

        DiagnosticResult result = await check.CheckAsync(
            CancellationToken.None);

        Assert.Equal(DiagnosticStatus.Passed, result.Status);
        Assert.Equal(DiagnosticSeverity.Info, result.Severity);
    }

    [Fact]
    public async Task CheckAsync_UnsupportedSchema_ReturnsFailed()
    {
        var inventory = new RouteInventory
        {
            SchemaVersion = 2,
            Routes = [ValidItem()]
        };

        var check = new RouteInventoryDiagnosticCheck(
            new FakeRouteInventoryPersistence(inventory));

        DiagnosticResult result = await check.CheckAsync(
            CancellationToken.None);

        Assert.Equal(DiagnosticStatus.Failed, result.Status);
        Assert.Equal(DiagnosticSeverity.Error, result.Severity);
        Assert.Contains("schema version", result.Message);
    }

    [Fact]
    public async Task CheckAsync_EmptyPrefix_ReturnsFailed()
    {
        var inventory = new RouteInventory
        {
            SchemaVersion = 1,
            Routes =
            [
                new RouteInventoryItem
                {
                    DestinationPrefix = "",
                    Gateway = "192.168.1.1",
                    InterfaceIndex = 1
                }
            ]
        };

        var check = new RouteInventoryDiagnosticCheck(
            new FakeRouteInventoryPersistence(inventory));

        DiagnosticResult result = await check.CheckAsync(
            CancellationToken.None);

        Assert.Equal(DiagnosticStatus.Failed, result.Status);
        Assert.Contains("DestinationPrefix", result.Message);
    }

    [Fact]
    public async Task CheckAsync_EmptyGateway_ReturnsFailed()
    {
        var inventory = new RouteInventory
        {
            SchemaVersion = 1,
            Routes =
            [
                new RouteInventoryItem
                {
                    DestinationPrefix = "10.0.0.0",
                    Gateway = "",
                    InterfaceIndex = 1
                }
            ]
        };

        var check = new RouteInventoryDiagnosticCheck(
            new FakeRouteInventoryPersistence(inventory));

        DiagnosticResult result = await check.CheckAsync(
            CancellationToken.None);

        Assert.Equal(DiagnosticStatus.Failed, result.Status);
        Assert.Contains("Gateway", result.Message);
    }

    [Fact]
    public async Task CheckAsync_DuplicatePrefixesDifferentGateways_ReturnsWarning()
    {
        var inventory = new RouteInventory
        {
            SchemaVersion = 1,
            Routes =
            [
                ValidItem(prefix: "10.0.0.0", gateway: "192.168.1.1"),
                ValidItem(prefix: "10.0.0.0", gateway: "192.168.1.2")
            ]
        };

        var check = new RouteInventoryDiagnosticCheck(
            new FakeRouteInventoryPersistence(inventory));

        DiagnosticResult result = await check.CheckAsync(
            CancellationToken.None);

        Assert.Equal(DiagnosticStatus.Warning, result.Status);
        Assert.Equal(DiagnosticSeverity.Warning, result.Severity);
        Assert.Contains("Duplicate prefixes", result.Message);
    }

    [Fact]
    public async Task CheckAsync_DuplicatePrefixesFirstMatch_Wins()
    {
        var inventory = new RouteInventory
        {
            SchemaVersion = 1,
            Routes =
            [
                ValidItem(prefix: "10.0.0.0", gateway: "192.168.1.1"),
                ValidItem(prefix: "10.0.0.0", gateway: "192.168.1.1")
            ]
        };

        var check = new RouteInventoryDiagnosticCheck(
            new FakeRouteInventoryPersistence(inventory));

        DiagnosticResult result = await check.CheckAsync(
            CancellationToken.None);

        Assert.Equal(DiagnosticStatus.Warning, result.Status);
    }

    [Fact]
    public async Task CheckAsync_DuplicateIdentity_ReturnsWarning()
    {
        var inventory = new RouteInventory
        {
            SchemaVersion = 1,
            Routes =
            [
                ValidItem(
                    prefix: "10.0.0.0",
                    gateway: "192.168.1.1",
                    interfaceIndex: 1),
                ValidItem(
                    prefix: "10.0.0.0",
                    gateway: "192.168.1.2",
                    interfaceIndex: 1)
            ]
        };

        var check = new RouteInventoryDiagnosticCheck(
            new FakeRouteInventoryPersistence(inventory));

        DiagnosticResult result = await check.CheckAsync(
            CancellationToken.None);

        Assert.Equal(DiagnosticStatus.Warning, result.Status);
    }

    [Fact]
    public async Task CheckAsync_ThrowsException_ReturnsFailed()
    {
        var check = new RouteInventoryDiagnosticCheck(
            new FakeRouteInventoryPersistence(
                new IOException("file not found")));

        DiagnosticResult result = await check.CheckAsync(
            CancellationToken.None);

        Assert.Equal(DiagnosticStatus.Failed, result.Status);
        Assert.Equal(DiagnosticSeverity.Error, result.Severity);
        Assert.NotNull(result.SuggestedAction);
    }

    [Fact]
    public async Task CheckAsync_EmptyRoutes_ReturnsPassed()
    {
        var inventory = new RouteInventory
        {
            SchemaVersion = 1,
            Routes = []
        };

        var check = new RouteInventoryDiagnosticCheck(
            new FakeRouteInventoryPersistence(inventory));

        DiagnosticResult result = await check.CheckAsync(
            CancellationToken.None);

        Assert.Equal(DiagnosticStatus.Passed, result.Status);
        Assert.Contains("0 route(s)", result.Message);
    }
}
