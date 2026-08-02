namespace IranDirect.Core.Tests.Observability;

using System.Net;
using IranDirect.Core.Configuration;
using IranDirect.Core.CustomRoutes;
using IranDirect.Core.Networking;
using IranDirect.Core.Observability;
using IranDirect.Core.Prefixes;
using IranDirect.Core.Routing;
using IranDirect.Core.Runtime;
using IranDirect.Core.Runtime.Execution;
using IranDirect.Core.Runtime.Profiling;
using IranDirect.Core.State;
using IranDirect.Core.Vpn;

public sealed class RuntimeSnapshotProviderTests
{
    private static readonly TimeSpan CacheDuration =
        TimeSpan.FromMinutes(30);

    private static readonly TimeSpan MaxStaleDuration =
        TimeSpan.FromHours(2);

    [Fact]
    public async Task GetSnapshotAsync_EmptyStores_ReturnsCompleteSnapshot()
    {
        await using Fixture fixture = Fixture.Create();

        RuntimeSnapshot snapshot =
            await fixture.Provider.GetSnapshotAsync();

        Assert.Equal(fixture.Clock.Now, snapshot.CapturedAt);
        Assert.Equal(1, snapshot.SchemaVersion);
        Assert.NotNull(snapshot.Configuration);
        Assert.False(snapshot.Configuration.Enabled);
        Assert.NotNull(snapshot.Runtime);
        Assert.NotNull(snapshot.Operation);
        Assert.Equal(OperationState.Idle, snapshot.Operation.State);
        Assert.Equal(1, snapshot.PrefixCount);
        Assert.Equal(0, snapshot.InstalledRouteCount);
        Assert.Equal(0, snapshot.RouteInventoryCount);
        Assert.NotNull(snapshot.VpnEndpointHealth);
        Assert.Equal(0, snapshot.VpnEndpointHealth.CurrentEndpointCount);
        Assert.False(snapshot.VpnEndpointHealth.IsProtected);
        Assert.Empty(snapshot.DnsCache);
        Assert.Null(snapshot.Performance);
        Assert.Null(snapshot.LastError);
        Assert.Null(snapshot.LastWarning);
    }

    [Fact]
    public async Task GetSnapshotAsync_AggregatesDesiredConfiguration()
    {
        await using Fixture fixture = Fixture.Create();
        await fixture.ConfigurationService.SetEnabledAsync(
            true,
            CancellationToken.None);

        RuntimeSnapshot snapshot =
            await fixture.Provider.GetSnapshotAsync();

        Assert.NotNull(snapshot.Configuration);
        Assert.True(snapshot.Configuration.Enabled);
        Assert.Equal(
            VpnProviderType.OpenVpn,
            snapshot.Configuration.VpnProvider);
    }

    [Fact]
    public async Task GetSnapshotAsync_ReflectsOperationState()
    {
        await using Fixture fixture = Fixture.Create();
        fixture.OperationStatus.Begin(
            OperationState.Enabling,
            "test-trigger");

        RuntimeSnapshot snapshot =
            await fixture.Provider.GetSnapshotAsync();

        Assert.NotNull(snapshot.Operation);
        Assert.Equal(
            OperationState.Enabling,
            snapshot.Operation.State);
        Assert.Equal(
            "test-trigger",
            snapshot.Operation.Trigger);
    }

    [Fact]
    public async Task GetSnapshotAsync_CountsRouteInventoryEntries()
    {
        await using Fixture fixture = Fixture.Create();
        await fixture.RouteInventoryStore.SaveAsync(
            new RouteInventory
            {
                Routes =
                [
                    new RouteInventoryItem
                    {
                        DestinationPrefix = "203.0.113.0/24",
                        Gateway = "192.0.2.1",
                        InterfaceIndex = 1
                    },
                    new RouteInventoryItem
                    {
                        DestinationPrefix = "198.51.100.0/24",
                        Gateway = "192.0.2.1",
                        InterfaceIndex = 1
                    }
                ]
            },
            CancellationToken.None);

        RuntimeSnapshot snapshot =
            await fixture.Provider.GetSnapshotAsync();

        Assert.Equal(2, snapshot.RouteInventoryCount);
        Assert.Equal(0, snapshot.InstalledRouteCount);
    }

    [Fact]
    public async Task GetSnapshotAsync_AggregatesDnsCacheStatuses()
    {
        await using Fixture fixture = Fixture.Create();
        CustomRouteEntry entry =
            await fixture.CustomRouteService.AddAsync(
                CustomRouteEntryType.Domain,
                "example.com",
                enabled: true,
                cancellationToken: CancellationToken.None);
        await fixture.DnsCacheRepository.UpsertSuccessAsync(
            entry.Id,
            "example.com",
            ["8.8.8.8"],
            CacheDuration,
            MaxStaleDuration,
            CancellationToken.None);

        RuntimeSnapshot snapshot =
            await fixture.Provider.GetSnapshotAsync();

        CustomRouteDnsCacheStatus status =
            Assert.Single(snapshot.DnsCache);
        Assert.Equal(entry.Id, status.CustomRouteEntryId);
        Assert.Equal("example.com", status.Domain);
        Assert.Equal(
            CustomRouteDnsCacheState.Fresh,
            status.State);
        Assert.Equal(["8.8.8.8"], status.IPv4Addresses);
    }

    [Fact]
    public async Task GetSnapshotAsync_WithoutPerfStore_PerformanceIsNull()
    {
        await using Fixture fixture = Fixture.Create();

        RuntimeSnapshot snapshot =
            await fixture.Provider.GetSnapshotAsync();

        Assert.Null(snapshot.Performance);
    }

    [Fact]
    public async Task GetSnapshotAsync_WithPerfStore_ReadsLatestReport()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "IranDirect.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            RuntimePerfReportStore store = new(directory);
            store.Write(new RuntimeCyclePerfReport
            {
                Trigger = "enable",
                StartedAt = new DateTimeOffset(
                    2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                CompletedAt = new DateTimeOffset(
                    2026, 1, 1, 0, 0, 1, TimeSpan.Zero),
                TotalMs = 12.5,
                Categories = []
            });

            await using Fixture fixture = Fixture.Create(store);

            RuntimeSnapshot snapshot =
                await fixture.Provider.GetSnapshotAsync();

            Assert.NotNull(snapshot.Performance);
            Assert.Equal("enable", snapshot.Performance.Trigger);
            Assert.Equal(12.5, snapshot.Performance.TotalMs);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task GetSnapshotAsync_ReportsLastErrorFromState()
    {
        await using Fixture fixture = Fixture.Create();
        await fixture.StateRepository.SaveAsync(
            new IranDirectState
            {
                Enabled = false,
                LastError = "boom"
            },
            CancellationToken.None);

        RuntimeSnapshot snapshot =
            await fixture.Provider.GetSnapshotAsync();

        Assert.Equal("boom", snapshot.LastError);
    }

    [Fact]
    public async Task GetSnapshotAsync_DoesNotMutateState()
    {
        await using Fixture fixture = Fixture.Create();
        await fixture.ConfigurationService.SetEnabledAsync(
            true,
            CancellationToken.None);

        await fixture.Provider.GetSnapshotAsync();
        await fixture.Provider.GetSnapshotAsync();

        DesiredConfiguration config =
            await fixture.ConfigurationService.GetAsync();
        IranDirectState state =
            await fixture.StateRepository.LoadAsync();
        RouteInventory inventory =
            await fixture.RouteInventoryStore.LoadAsync();

        Assert.True(config.Enabled);
        Assert.False(state.Enabled);
        Assert.Empty(inventory.Routes);
        Assert.Empty(
            await fixture.CustomRouteService.GetAllAsync());
    }

    [Fact]
    public void Snapshot_IsImmutableRecord()
    {
        RuntimeSnapshot original = new()
        {
            CapturedAt = DateTimeOffset.UtcNow,
            Configuration = new DesiredConfiguration
            {
                Enabled = true
            }
        };

        RuntimeSnapshot updated =
            original with { Configuration = null };

        Assert.NotNull(original.Configuration);
        Assert.True(original.Configuration.Enabled);
        Assert.Null(updated.Configuration);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _directory;

        public FakeTimeProvider Clock { get; }
        public StateRepository StateRepository { get; }
        public RouteInventoryStore RouteInventoryStore { get; }
        public DesiredConfigurationService ConfigurationService
            { get; }
        public CustomRouteService CustomRouteService { get; }
        public ICustomRouteDnsCacheRepository DnsCacheRepository
            { get; }
        public RuntimeOperationStatus OperationStatus { get; }
        public RuntimeSnapshotProvider Provider { get; }

        private Fixture(
            string directory,
            FakeTimeProvider clock,
            StateRepository stateRepository,
            RouteInventoryStore routeInventoryStore,
            DesiredConfigurationService configurationService,
            CustomRouteService customRouteService,
            ICustomRouteDnsCacheRepository dnsCacheRepository,
            RuntimeOperationStatus operationStatus,
            RuntimeSnapshotProvider provider)
        {
            _directory = directory;
            Clock = clock;
            StateRepository = stateRepository;
            RouteInventoryStore = routeInventoryStore;
            ConfigurationService = configurationService;
            CustomRouteService = customRouteService;
            DnsCacheRepository = dnsCacheRepository;
            OperationStatus = operationStatus;
            Provider = provider;
        }

        public static Fixture Create(
            RuntimePerfReportStore? perfStore = null)
        {
            string directory = Path.Combine(
                Path.GetTempPath(),
                "IranDirect.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);

            FakeTimeProvider clock = new(
                new DateTimeOffset(
                    2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

            StateRepository stateRepository = new(
                Path.Combine(directory, "state.json"));
            RouteInventoryStore routeInventoryStore = new(
                Path.Combine(directory, "route-inventory.json"));
            VpnEndpointInventoryStore endpointInventory = new(
                Path.Combine(directory, "endpoint-inventory.json"));
            PrefixFileRepository prefixRepo = new(
                Path.Combine(directory, "prefixes.txt"));
            File.WriteAllText(
                Path.Combine(directory, "prefixes.txt"),
                "203.0.113.0/24" + Environment.NewLine);

            DesiredConfigurationStore configStore = new(
                Path.Combine(directory, "config.json"),
                new DesiredConfigurationValidator());
            DesiredConfigurationService configurationService =
                new(configStore);

            FakeExecutor executor = new();
            FakeDecisionBuilder decisionBuilder = new();
            RuntimeCycleCoordinator coordinator =
                new(decisionBuilder);
            RuntimeOperationStatus operationStatus = new();

            FakeRouteManager routeManager = new();
            GatewayDetector gatewayDetector = new();
            OpenVpnEndpointProvider vpnProvider = new(
                Path.Combine(directory, "vpn-profile.ovpn"),
                new OpenVpnProfileParser(),
                new VpnEndpointResolver());
            VpnEndpointRouteManager vpnRouteManager =
                new(routeManager);

            IranDirectController controller = new(
                null!,
                prefixRepo,
                gatewayDetector,
                routeManager,
                stateRepository,
                routeInventoryStore,
                vpnProvider,
                vpnRouteManager,
                endpointInventory,
                coordinator,
                executor,
                configurationService,
                operationStatus);

            CustomRouteRepository customRouteRepository = new(
                new CustomRouteStore(
                    Path.Combine(directory, "custom-routes.json")));
            CustomRouteService customRouteService = new(
                customRouteRepository,
                new CustomRouteEntryValidator(),
                clock);
            CustomRouteDnsCacheRepository dnsCacheRepository = new(
                new CustomRouteDnsCacheStore(
                    Path.Combine(directory, "custom-route-dns-cache.json")),
                clock);
            CustomRouteDnsCacheService dnsCacheService = new(
                customRouteService,
                dnsCacheRepository,
                clock);

            RuntimeSnapshotProvider provider = new(
                controller,
                configurationService,
                routeInventoryStore,
                dnsCacheService,
                perfStore,
                clock);

            return new Fixture(
                directory,
                clock,
                stateRepository,
                routeInventoryStore,
                configurationService,
                customRouteService,
                dnsCacheRepository,
                operationStatus,
                provider);
        }

        public async ValueTask DisposeAsync()
        {
            try { Directory.Delete(_directory, recursive: true); }
            catch { }
            await ValueTask.CompletedTask;
        }
    }

    internal sealed class FakeDecisionBuilder : IRuntimeDecisionBuilder
    {
        public RuntimeDecision? Decision { get; set; }

        public Task<RuntimeDecision> BuildAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Decision!);
        }
    }

    internal sealed class FakeExecutor : IRuntimeExecutor
    {
        public RuntimeExecutionResult Result { get; set; } =
            RuntimeExecutionResult.NoExecutionRequired();

        public Task<RuntimeExecutionResult> ExecuteAsync(
            RuntimeExecutionPlan plan,
            IProgress<RuntimeExecutionProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Result);
        }
    }

    internal sealed class FakeRouteManager : IRouteManager
    {
        public HashSet<string> Present { get; set; } = [];

        public Task AddRoutesAsync(
            IReadOnlyCollection<ManagedRoute> routes,
            CancellationToken cancellationToken = default)
        {
            foreach (ManagedRoute route in routes)
                Present.Add(route.Identity);
            return Task.CompletedTask;
        }

        public Task DeleteRoutesAsync(
            IReadOnlyCollection<ManagedRoute> routes,
            CancellationToken cancellationToken = default)
        {
            foreach (ManagedRoute route in routes)
                Present.Remove(route.Identity);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<SystemRoute>> GetIpv4RoutesAsync(
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<SystemRoute> routes = Present
                .Select(id =>
                {
                    string[] parts = id.Split('|', 3);
                    return new SystemRoute
                    {
                        DestinationPrefix = parts[0],
                        NextHop = IPAddress.Parse(parts[1]),
                        InterfaceIndex = uint.Parse(parts[2])
                    };
                })
                .ToArray();
            return Task.FromResult(routes);
        }
    }

    private sealed class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset _now;

        public FakeTimeProvider(DateTimeOffset now)
        {
            _now = now;
        }

        public DateTimeOffset Now => _now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan duration) =>
            _now += duration;
    }
}
