using System.Net;
using PathVeer.Core.Diagnostics;
using PathVeer.Core.Diagnostics.Routing;
using PathVeer.Core.Routing;

namespace PathVeer.Core.Tests.Diagnostics.Routing;

public sealed class WindowsRouteTableDiagnosticCheckTests
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

    private static SystemRoute Route(
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

    [Fact]
    public async Task CheckAsync_EmptyTable_ReturnsPassed()
    {
        var check =
            new WindowsRouteTableDiagnosticCheck(
                new FakeRouteManager());

        DiagnosticResult result =
            await check.CheckAsync(CancellationToken.None);

        Assert.Equal(DiagnosticStatus.Passed, result.Status);
        Assert.Equal(DiagnosticSeverity.Info, result.Severity);
    }

    [Fact]
    public async Task CheckAsync_ValidRoutes_ReturnsPassed()
    {
        var manager = new FakeRouteManager(
            new[] { Route(), Route("10.0.1.0/24", "10.0.0.1", 2) });

        var check =
            new WindowsRouteTableDiagnosticCheck(manager);

        DiagnosticResult result =
            await check.CheckAsync(CancellationToken.None);

        Assert.Equal(DiagnosticStatus.Passed, result.Status);
        Assert.Contains("2 route(s)", result.Message);
    }

    [Fact]
    public async Task CheckAsync_DuplicateRoutes_ReturnsWarning()
    {
        var manager = new FakeRouteManager(
            new[] { Route(), Route() });

        var check =
            new WindowsRouteTableDiagnosticCheck(manager);

        DiagnosticResult result =
            await check.CheckAsync(CancellationToken.None);

        Assert.Equal(DiagnosticStatus.Warning, result.Status);
        Assert.Equal(DiagnosticSeverity.Warning, result.Severity);
    }

    [Fact]
    public async Task CheckAsync_EmptyPrefix_ReturnsWarning()
    {
        var manager = new FakeRouteManager(
            new[] { Route(prefix: "") });

        var check =
            new WindowsRouteTableDiagnosticCheck(manager);

        DiagnosticResult result =
            await check.CheckAsync(CancellationToken.None);

        Assert.Equal(DiagnosticStatus.Warning, result.Status);
    }

    [Fact]
    public async Task CheckAsync_ZeroInterfaceIndex_ReturnsWarning()
    {
        var manager = new FakeRouteManager(
            new[] { Route(iface: 0) });

        var check =
            new WindowsRouteTableDiagnosticCheck(manager);

        DiagnosticResult result =
            await check.CheckAsync(CancellationToken.None);

        Assert.Equal(DiagnosticStatus.Warning, result.Status);
    }

    [Fact]
    public async Task CheckAsync_ReadException_ReturnsFailed()
    {
        var manager = new FakeRouteManager(
            exception: new IOException("access denied"));

        var check =
            new WindowsRouteTableDiagnosticCheck(manager);

        DiagnosticResult result =
            await check.CheckAsync(CancellationToken.None);

        Assert.Equal(DiagnosticStatus.Failed, result.Status);
        Assert.Equal(
            DiagnosticSeverity.Error, result.Severity);
        Assert.NotNull(result.SuggestedAction);
    }

    [Fact]
    public void CheckAsync_HasCorrectIdAndTitle()
    {
        var check =
            new WindowsRouteTableDiagnosticCheck(
                new FakeRouteManager());

        Assert.Equal("windows-route-table", check.Id);
        Assert.Equal("Windows route table", check.Title);
    }

    [Fact]
    public async Task CheckAsync_LoopbackRoutes_Skipped()
    {
        var manager = new FakeRouteManager(
            new[]
            {
                Route(gateway: "127.0.0.1"),
                Route(prefix: "10.0.1.0/24")
            });

        var check =
            new WindowsRouteTableDiagnosticCheck(manager);

        DiagnosticResult result =
            await check.CheckAsync(CancellationToken.None);

        Assert.Equal(DiagnosticStatus.Passed, result.Status);
        Assert.Contains("2 route(s)", result.Message);
    }
}
