using IranDirect.Core.Configuration;
using IranDirect.Core.Networking;
using IranDirect.Core.Prefixes;
using IranDirect.Core.Routing;
using IranDirect.Core.Runtime;
using IranDirect.Core.Runtime.Execution;
using IranDirect.Core.Runtime.Reconciliation;
using IranDirect.Core.State;
using IranDirect.Core.Vpn;

namespace IranDirect.Core.Tests.Prefixes;

public sealed class PrefixUpdateHistoryIntegrationTests
{
    private static readonly DateTimeOffset BaseTime =
        new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task UpdatePrefixes_Success_AppendsSucceededHistoryEntry()
    {
        await using Fixture fixture = new();
        fixture.Source.Prefixes =
        [
            "1.2.3.0/24",
            "10.0.0.0/8"
        ];
        fixture.Clock.Now = BaseTime;

        int count = await fixture.Controller.UpdatePrefixesAsync();

        Assert.Equal(2, count);

        PrefixSourceUpdateHistoryEntry entry =
            Assert.Single(await fixture.HistoryService.GetRecentAsync());

        Assert.Equal(
            PrefixSourceUpdateStatus.Succeeded,
            entry.Status);
        Assert.Equal(
            OfficialIranPrefixSource.Descriptor.Id,
            entry.SourceId);
        Assert.Equal(2, entry.PrefixCount);
        Assert.Equal(2, entry.AddedCount);
        Assert.Equal(0, entry.RemovedCount);
        Assert.Equal(0, entry.UnchangedCount);
        Assert.True(entry.HasChanges);
        Assert.Equal(
            PrefixContentHasher.ComputeHash(
                fixture.Source.Prefixes),
            entry.CurrentContentHash);
        Assert.Equal(BaseTime, entry.AttemptedAt);
        Assert.Null(entry.Error);
    }

    [Fact]
    public async Task UpdatePrefixes_ThenNotModified_AppendsBothEntries()
    {
        await using Fixture fixture = new();
        fixture.Source.Prefixes =
        [
            "1.2.3.0/24",
            "10.0.0.0/8"
        ];
        fixture.Clock.Now = BaseTime;

        await fixture.Controller.UpdatePrefixesAsync();

        fixture.Source.NotModified = true;
        fixture.Clock.Now = BaseTime.AddHours(1);

        await fixture.Controller.UpdatePrefixesAsync();

        IReadOnlyList<PrefixSourceUpdateHistoryEntry> recent =
            await fixture.HistoryService.GetRecentAsync();

        Assert.Equal(2, recent.Count);

        PrefixSourceUpdateHistoryEntry first = recent[0];
        Assert.Equal(
            PrefixSourceUpdateStatus.NotModified,
            first.Status);
        Assert.Equal(2, first.PrefixCount);
        Assert.Equal(2, first.UnchangedCount);
        Assert.False(first.HasChanges);
        Assert.Equal(
            BaseTime.AddHours(1),
            first.AttemptedAt);
        Assert.Equal(
            PrefixContentHasher.ComputeHash(
                fixture.Source.Prefixes),
            first.CurrentContentHash);

        PrefixSourceUpdateHistoryEntry second = recent[1];
        Assert.Equal(
            PrefixSourceUpdateStatus.Succeeded,
            second.Status);
        Assert.Equal(BaseTime, second.AttemptedAt);
    }

    [Fact]
    public async Task UpdatePrefixes_FailedDownload_AppendsFailureEntry()
    {
        await using Fixture fixture = new();
        fixture.Source.ExceptionToThrow =
            new InvalidOperationException("network down");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Controller.UpdatePrefixesAsync());

        PrefixSourceUpdateHistoryEntry entry =
            Assert.Single(await fixture.HistoryService.GetRecentAsync());

        Assert.Equal(
            PrefixSourceUpdateStatus.Failed,
            entry.Status);
        Assert.Equal("network down", entry.Error);
        Assert.Equal(0, entry.PrefixCount);
        Assert.False(entry.HasChanges);
        Assert.False(File.Exists(fixture.PrefixFilePath));
    }

    [Fact]
    public async Task UpdatePrefixes_HistoryWriteFails_UpdateStillSucceeds()
    {
        await using Fixture fixture = new();
        fixture.Source.Prefixes =
        [
            "1.2.3.0/24",
            "10.0.0.0/8"
        ];
        ThrowingHistoryService throwing = new();

        IranDirectController controller =
            fixture.CreateController(
                fixture.Source,
                fixture.MetadataService,
                throwing);

        int count =
            await controller.UpdatePrefixesAsync();

        Assert.Equal(2, count);
        Assert.True(File.Exists(fixture.PrefixFilePath));
        Assert.Single(throwing.RecordSuccessCalls);
    }

    [Fact]
    public async Task UpdatePrefixes_TwoAttempts_AppendsOneEntryPerAttempt()
    {
        await using Fixture fixture = new();
        fixture.Source.Prefixes =
        [
            "1.2.3.0/24",
            "10.0.0.0/8"
        ];

        await fixture.Controller.UpdatePrefixesAsync();

        fixture.Source.Prefixes =
        [
            "1.2.3.0/24",
            "10.0.0.0/8",
            "5.6.7.0/24"
        ];

        await fixture.Controller.UpdatePrefixesAsync();

        IReadOnlyList<PrefixSourceUpdateHistoryEntry> recent =
            await fixture.HistoryService.GetRecentAsync();

        Assert.Equal(2, recent.Count);
        Assert.Equal(
            2,
            recent.Select(entry => entry.Id).Distinct().Count());
        Assert.Equal(3, recent[0].PrefixCount);
        Assert.Equal(2, recent[1].PrefixCount);
    }

    [Fact]
    public async Task UpdatePrefixes_HistoryFile_DoesNotRetainFullDatasets()
    {
        await using Fixture fixture = new();
        fixture.Source.Prefixes =
        [
            "1.2.3.0/24",
            "10.0.0.0/8"
        ];

        await fixture.Controller.UpdatePrefixesAsync();

        string content =
            await File.ReadAllTextAsync(fixture.HistoryFilePath);

        Assert.DoesNotContain("1.2.3.0/24", content);
        Assert.Contains("\"AddedCount\": 2", content);
    }

    private sealed class ThrowingHistoryService :
        IPrefixSourceUpdateHistoryService
    {
        public List<int> RecordSuccessCalls { get; } = [];

        public Task<IReadOnlyList<PrefixSourceUpdateHistoryEntry>>
            GetRecentAsync(
                int? limit = null,
                CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<
                PrefixSourceUpdateHistoryEntry>>([]);

        public Task RecordSuccessAsync(
            PrefixSourceFetchResult result,
            PrefixSourceChangeSummary? changeSummary = null,
            IReadOnlyList<string>? previousPrefixes = null,
            CancellationToken cancellationToken = default)
        {
            RecordSuccessCalls.Add(1);
            throw new InvalidOperationException(
                "history disk full");
        }

        public Task RecordNotModifiedAsync(
            PrefixSourceFetchResult result,
            int currentPrefixCount = 0,
            string? currentContentHash = null,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task RecordFailureAsync(
            PrefixSourceDescriptor source,
            string error,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task ClearAsync(
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class FakePrefixSource : IPrefixSource
    {
        public PrefixSourceDescriptor Descriptor =>
            OfficialIranPrefixSource.Descriptor;

        public IReadOnlyList<string> Prefixes { get; set; } = [];
        public Exception? ExceptionToThrow { get; set; }
        public bool NotModified { get; set; }

        public Task<PrefixSourceFetchResult> FetchAsync(
            PrefixSourceRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (ExceptionToThrow is not null)
            {
                throw ExceptionToThrow;
            }

            if (NotModified)
            {
                return Task.FromResult(
                    new PrefixSourceFetchResult
                    {
                        Source = Descriptor,
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
                    Source = Descriptor,
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
        public string HistoryFilePath { get; }
        public PrefixFileRepository PrefixRepository { get; }
        public StateRepository StateRepository { get; }
        public PrefixSourceMetadataService MetadataService { get; }
        public PrefixSourceUpdateHistoryService HistoryService { get; }
        public FakeTimeProvider Clock { get; } = new();
        public FakePrefixSource Source { get; } = new();
        public IranDirectController Controller { get; }

        public Fixture()
        {
            _tempDir = Path.Combine(
                Path.GetTempPath(),
                $"IranDirectTest_{Guid.NewGuid()}");
            Directory.CreateDirectory(_tempDir);

            PrefixFilePath = Path.Combine(
                _tempDir, "prefixes.txt");
            HistoryFilePath = Path.Combine(
                _tempDir, "prefix-source-update-history.json");
            PrefixRepository =
                new PrefixFileRepository(PrefixFilePath);
            StateRepository = new StateRepository(
                Path.Combine(_tempDir, "state.json"));
            _routeInventory = new RouteInventoryStore(
                Path.Combine(
                    _tempDir, "route-inventory.json"));
            _endpointInventory =
                new VpnEndpointInventoryStore(
                    Path.Combine(
                        _tempDir, "endpoint-inventory.json"));

            PrefixSourceMetadataStore metadataStore = new(
                Path.Combine(
                    _tempDir, "prefix-source-metadata.json"));
            MetadataService = new PrefixSourceMetadataService(
                new PrefixSourceMetadataRepository(
                    metadataStore,
                    new PrefixSourceMetadataValidator()),
                Clock);

            HistoryService = new PrefixSourceUpdateHistoryService(
                new PrefixSourceUpdateHistoryRepository(
                    new PrefixSourceUpdateHistoryStore(
                        HistoryFilePath),
                    new PrefixSourceUpdateHistoryValidator()),
                options: null,
                Clock);

            Controller = CreateController(
                Source,
                MetadataService,
                HistoryService);
        }

        public IranDirectController CreateController(
            IPrefixSource source,
            IPrefixSourceMetadataService? metadataService = null,
            IPrefixSourceUpdateHistoryService? historyService = null)
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

            return new IranDirectController(
                source,
                PrefixRepository,
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
                    metadataService,
                prefixSourceUpdateHistoryService:
                    historyService);
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
