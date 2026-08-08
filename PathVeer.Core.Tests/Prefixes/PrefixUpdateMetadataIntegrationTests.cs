using PathVeer.Core.Configuration;
using PathVeer.Core.Networking;
using PathVeer.Core.Prefixes;
using PathVeer.Core.Routing;
using PathVeer.Core.Runtime;
using PathVeer.Core.Runtime.Execution;
using PathVeer.Core.Runtime.Reconciliation;
using PathVeer.Core.State;
using PathVeer.Core.Vpn;

namespace PathVeer.Core.Tests.Prefixes;

public sealed class PrefixUpdateMetadataIntegrationTests
{
    private static readonly DateTimeOffset BaseTime =
        new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly DirectCountryCode Country =
        DirectCountryCode.IR;

    [Fact]
    public async Task UpdatePrefixes_Success_RecordsMetadata()
    {
        await using Fixture fixture = new();
        fixture.Source.Prefixes =
        [
            "1.2.3.0/24",
            "10.0.0.0/8"
        ];
        fixture.Clock.Now = BaseTime;

        int count = await fixture.Controller.UpdatePrefixesAsync(Country);

        Assert.Equal(2, count);

        PrefixSourceMetadata? metadata =
            await fixture.MetadataService.GetCurrentAsync(Country);

        Assert.NotNull(metadata);
        Assert.Equal(
            PrefixSourceUpdateStatus.Succeeded,
            metadata.LastStatus);
        Assert.Equal(
            fixture.Source.GetDescriptor(Country).Id,
            metadata.SourceId);
        Assert.Equal(2, metadata.PrefixCount);
        Assert.Equal(
            PrefixContentHasher.ComputeHash(
                fixture.Source.Prefixes),
            metadata.ContentHash);
        Assert.Equal(BaseTime, metadata.LastAttemptedAt);
        Assert.Equal(BaseTime, metadata.LastSucceededAt);
        Assert.Null(metadata.LastError);

        PathVeerState state =
            await fixture.StateRepository.LoadAsync();
        Assert.Equal(2, state.PrefixCount);
        Assert.NotNull(state.PrefixesUpdatedAt);
    }

    [Fact]
    public async Task UpdatePrefixes_FailedDownload_RecordsFailure()
    {
        await using Fixture fixture = new();
        fixture.Source.ExceptionToThrow =
            new InvalidOperationException("network down");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Controller.UpdatePrefixesAsync(Country));

        PrefixSourceMetadata? metadata =
            await fixture.MetadataService.GetCurrentAsync(Country);

        Assert.NotNull(metadata);
        Assert.Equal(
            PrefixSourceUpdateStatus.Failed,
            metadata.LastStatus);
        Assert.Equal("network down", metadata.LastError);
        Assert.Equal(0, metadata.PrefixCount);
        Assert.False(File.Exists(fixture.PrefixFilePath));
    }

    [Fact]
    public async Task UpdatePrefixes_PrefixPersistenceSucceeds_WhenMetadataWriteFails()
    {
        await using Fixture fixture = new();
        fixture.Source.Prefixes =
        [
            "1.2.3.0/24",
            "10.0.0.0/8"
        ];
        ThrowingMetadataService throwing = new();

        PathVeerController controller =
            fixture.CreateController(
                fixture.Source,
                throwing);

        int count =
            await controller.UpdatePrefixesAsync(Country);

        Assert.Equal(2, count);
        Assert.True(File.Exists(fixture.PrefixFilePath));
        Assert.Single(throwing.RecordSuccessCalls);
    }

    [Fact]
    public async Task UpdatePrefixes_PrefixFileOutput_MatchesPreSliceFormat()
    {
        await using Fixture fixture = new();
        fixture.Source.Prefixes =
        [
            "1.2.3.0/24",
            "10.0.0.0/8"
        ];

        await fixture.Controller.UpdatePrefixesAsync(Country);

        string content =
            await File.ReadAllTextAsync(fixture.PrefixFilePath);

        string expected =
            string.Join(
                Environment.NewLine,
                fixture.Source.Prefixes)
            + Environment.NewLine;

        Assert.Equal(expected, content);
    }

    [Fact]
    public async Task UpdatePrefixes_NotModified_RecordsNotModified()
    {
        await using Fixture fixture = new();
        fixture.Source.NotModified = true;

        int count = await fixture.Controller.UpdatePrefixesAsync(Country);

        Assert.Equal(0, count);

        PrefixSourceMetadata? metadata =
            await fixture.MetadataService.GetCurrentAsync(Country);

        Assert.NotNull(metadata);
        Assert.Equal(
            PrefixSourceUpdateStatus.NotModified,
            metadata.LastStatus);
    }

    private sealed class ThrowingMetadataService :
        IPrefixSourceMetadataService
    {
        public List<int> RecordSuccessCalls { get; } = [];

        public Task<PrefixSourceMetadata?> GetCurrentAsync(
            DirectCountryCode country,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<PrefixSourceMetadata?>(null);

        public Task<PrefixSourceChangeSummary?>
            GetLatestChangeSummaryAsync(
                DirectCountryCode country,
                CancellationToken cancellationToken = default) =>
            Task.FromResult<PrefixSourceChangeSummary?>(null);

        public Task RecordSuccessAsync(
            DirectCountryCode country,
            PrefixSourceFetchResult result,
            IReadOnlyList<string>? previousPrefixes = null,
            CancellationToken cancellationToken = default)
        {
            RecordSuccessCalls.Add(1);
            throw new InvalidOperationException(
                "metadata disk full");
        }

        public Task RecordNotModifiedAsync(
            DirectCountryCode country,
            PrefixSourceFetchResult result,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task RecordFailureAsync(
            DirectCountryCode country,
            PrefixSourceDescriptor source,
            string error,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class FakePrefixSource : ICountryPrefixSource
    {
        public IReadOnlyList<string> Prefixes { get; set; } = [];
        public Exception? ExceptionToThrow { get; set; }
        public bool NotModified { get; set; }

        public PrefixSourceDescriptor GetDescriptor(
            DirectCountryCode country) =>
            new()
            {
                Id = "ripe-stat-country-resource-list-ipv4",
                DisplayName =
                    "RIPEstat country resource list (IPv4)",
                Uri = 
                    "https://stat.ripe.net/data/" +
                    "country-resource-list/data.json" +
                    $"?resource={country.Code}",
                Format = "ripestat-country-resource-list-json",
                ParserVersion = "1"
            };

        public Task<PrefixSourceFetchResult> FetchAsync(
            DirectCountryCode country,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (ExceptionToThrow is not null)
            {
                throw ExceptionToThrow;
            }

            PrefixSourceDescriptor descriptor = GetDescriptor(country);

            if (NotModified)
            {
                return Task.FromResult(
                    new PrefixSourceFetchResult
                    {
                        Source = descriptor,
                        CountryCode = country,
                        Prefixes = [],
                        StartedAt = BaseTime,
                        CompletedAt = BaseTime.AddSeconds(1),
                        Duration = TimeSpan.FromSeconds(1),
                        NotModified = true
                    });
            }

            return Task.FromResult(
                new PrefixSourceFetchResult
                {
                    Source = descriptor,
                    CountryCode = country,
                    Prefixes = Prefixes,
                    StartedAt = BaseTime,
                    CompletedAt = BaseTime.AddSeconds(3),
                    Duration = TimeSpan.FromSeconds(3),
                    ContentHash =
                        PrefixContentHasher.ComputeHash(
                            Prefixes),
                    ContentLength = 100
                });
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _tempDir;
        private readonly RouteInventoryStore _routeInventory;
        private readonly VpnEndpointInventoryStore
            _endpointInventory;

        public string PrefixFilePath { get; }
        public CountryPrefixStore PrefixStore { get; }
        public StateRepository StateRepository { get; }
        public PrefixSourceMetadataService MetadataService { get; }
        public FakeTimeProvider Clock { get; } = new();
        public FakePrefixSource Source { get; } = new();
        public PathVeerController Controller { get; }

        public Fixture()
        {
            _tempDir = Path.Combine(
                Path.GetTempPath(),
                $"IranDirectTest_{Guid.NewGuid()}");
            Directory.CreateDirectory(_tempDir);

            PrefixFilePath = Path.Combine(
                _tempDir, "prefixes", "IR", "ipv4-prefixes.txt");
            PrefixStore = new CountryPrefixStore(_tempDir);
            StateRepository = new StateRepository(
                Path.Combine(_tempDir, "state.json"));
            _routeInventory = new RouteInventoryStore(
                Path.Combine(
                    _tempDir, "route-inventory.json"));
            _endpointInventory =
                new VpnEndpointInventoryStore(
                    Path.Combine(
                        _tempDir, "endpoint-inventory.json"));

            MetadataService = new PrefixSourceMetadataService(
                PrefixStore,
                Clock);

            Controller = CreateController(
                Source,
                MetadataService);
        }

        public PathVeerController CreateController(
            ICountryPrefixSource source,
            IPrefixSourceMetadataService? metadataService = null)
        {
            FakeRouteManager routeManager = new();
            GatewayDetector gatewayDetector = new();
            OpenVpnEndpointProvider vpnProvider = new(
                Path.Combine(_tempDir, "vpn-profile.ovpn"),
                new OpenVpnProfileParser(),
                new VpnEndpointResolver());
            VpnEndpointRouteManager vpnRouteManager =
                new(routeManager);

            DesiredConfigurationStore configStore = new(
                Path.Combine(_tempDir, "config.json"),
                new DesiredConfigurationValidator());

            RuntimeCycleCoordinator coordinator = new(
                new FakeDecisionBuilder());

            return new PathVeerController(
                source,
                PrefixStore,
                gatewayDetector,
                routeManager,
                StateRepository,
                _routeInventory,
                vpnProvider,
                vpnRouteManager,
                _endpointInventory,
                coordinator,
                new FakeExecutor(),
                new DesiredConfigurationService(configStore),
                new RuntimeOperationStatus(),
                prefixSourceMetadataService:
                    metadataService);
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                Directory.Delete(_tempDir, recursive: true);
            }
            catch
            {
                // best effort cleanup
            }

            await ValueTask.CompletedTask;
        }
    }

    private sealed class FakeDecisionBuilder :
        IRuntimeDecisionBuilder
    {
        public Task<RuntimeDecision> BuildAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new NotSupportedException(
                "Not exercised by prefix update tests.");
        }
    }

    private sealed class FakeExecutor : IRuntimeExecutor
    {
        public Task<RuntimeExecutionResult> ExecuteAsync(
            RuntimeExecutionPlan plan,
            IProgress<RuntimeExecutionProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(
                RuntimeExecutionResult.NoExecutionRequired());
        }
    }

    private sealed class FakeRouteManager : IRouteManager
    {
        public Task AddRoutesAsync(
            IReadOnlyCollection<ManagedRoute> routes,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task DeleteRoutesAsync(
            IReadOnlyCollection<ManagedRoute> routes,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<SystemRoute>> GetIpv4RoutesAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SystemRoute>>([]);
    }

    private sealed class FakeTimeProvider : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = BaseTime;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
