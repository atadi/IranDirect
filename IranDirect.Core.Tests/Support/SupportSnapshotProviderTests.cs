using System.Reflection;
using IranDirect.Core.Configuration;
using IranDirect.Core.CustomRoutes;
using IranDirect.Core.Diagnostics;
using IranDirect.Core.Observability;
using IranDirect.Core.Planning;
using IranDirect.Core.Prefixes;
using IranDirect.Core.Runtime;
using IranDirect.Core.Runtime.Profiling;
using IranDirect.Core.Support;

namespace IranDirect.Core.Tests.Support;

public sealed class SupportSnapshotProviderTests
{
    private static readonly DateTimeOffset FixedTime =
        new(2026, 8, 3, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Constructor_NullRuntimeSnapshotProvider_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => new SupportSnapshotProvider(
                null!,
                new FakeDiagnosticRunner(),
                new FakePreviewPlanner(),
                new FakeConfigurationService(),
                new FakePrefixMetadataService(),
                new FakePrefixHistoryService(),
                new FakeDnsCacheService()));
    }

    [Fact]
    public void Constructor_NullDiagnosticRunner_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => new SupportSnapshotProvider(
                new FakeRuntimeSnapshotProvider(),
                null!,
                new FakePreviewPlanner(),
                new FakeConfigurationService(),
                new FakePrefixMetadataService(),
                new FakePrefixHistoryService(),
                new FakeDnsCacheService()));
    }

    [Fact]
    public void Constructor_NullPreviewPlanner_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => new SupportSnapshotProvider(
                new FakeRuntimeSnapshotProvider(),
                new FakeDiagnosticRunner(),
                null!,
                new FakeConfigurationService(),
                new FakePrefixMetadataService(),
                new FakePrefixHistoryService(),
                new FakeDnsCacheService()));
    }

    [Fact]
    public void Constructor_NullConfigurationService_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => new SupportSnapshotProvider(
                new FakeRuntimeSnapshotProvider(),
                new FakeDiagnosticRunner(),
                new FakePreviewPlanner(),
                null!,
                new FakePrefixMetadataService(),
                new FakePrefixHistoryService(),
                new FakeDnsCacheService()));
    }

    [Fact]
    public void Constructor_NullPrefixMetadataService_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => new SupportSnapshotProvider(
                new FakeRuntimeSnapshotProvider(),
                new FakeDiagnosticRunner(),
                new FakePreviewPlanner(),
                new FakeConfigurationService(),
                null!,
                new FakePrefixHistoryService(),
                new FakeDnsCacheService()));
    }

    [Fact]
    public void Constructor_NullPrefixHistoryService_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => new SupportSnapshotProvider(
                new FakeRuntimeSnapshotProvider(),
                new FakeDiagnosticRunner(),
                new FakePreviewPlanner(),
                new FakeConfigurationService(),
                new FakePrefixMetadataService(),
                null!,
                new FakeDnsCacheService()));
    }

    [Fact]
    public void Constructor_NullDnsCacheService_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => new SupportSnapshotProvider(
                new FakeRuntimeSnapshotProvider(),
                new FakeDiagnosticRunner(),
                new FakePreviewPlanner(),
                new FakeConfigurationService(),
                new FakePrefixMetadataService(),
                new FakePrefixHistoryService(),
                null!));
    }

    [Fact]
    public async Task CaptureAsync_ReturnsAggregatedSnapshot()
    {
        FakeRuntimeSnapshotProvider runtime = new(prefixCount: 10);
        FakeDiagnosticRunner diagnostics = new(
            passed: 2, warnings: 1, failed: 0);
        FakePreviewPlanner planner = new(hasChanges: true);
        FakeConfigurationService configuration = new();
        FakePrefixMetadataService metadata =
            new(prefixCount: 10);
        FakePrefixHistoryService history = new(
            entries: [new PrefixSourceUpdateHistoryEntry
            {
                SourceId = "s1",
                Status = PrefixSourceUpdateStatus.Succeeded,
                StartedAt = FixedTime,
                CompletedAt = FixedTime,
                AttemptedAt = FixedTime
            }]);
        FakeDnsCacheService dnsCache = new(domainCount: 3);

        SupportSnapshotProvider provider = new(
            runtime,
            diagnostics,
            planner,
            configuration,
            metadata,
            history,
            dnsCache,
            timeProvider: new FixedTimeProvider(FixedTime));

        SupportSnapshot snapshot =
            await provider.CaptureAsync();

        Assert.Equal(FixedTime, snapshot.CapturedAt);
        Assert.NotNull(snapshot.Runtime);
        Assert.NotNull(snapshot.Diagnostics);
        Assert.NotNull(snapshot.ExecutionPreview);
        Assert.NotNull(snapshot.Configuration);
        Assert.NotNull(snapshot.PrefixMetadata);
        Assert.Single(snapshot.PrefixHistory);
        Assert.Equal(3, snapshot.DnsCache.Count);
        Assert.Null(snapshot.Performance);
        Assert.Equal(2, snapshot.Summary.DiagnosticPassedCount);
        Assert.Equal(1, snapshot.Summary.DiagnosticWarningCount);
        Assert.Equal(0, snapshot.Summary.DiagnosticFailedCount);
        Assert.Equal(10, snapshot.Summary.PrefixCount);
        Assert.Equal(3, snapshot.Summary.DnsDomainCount);
        Assert.True(snapshot.Summary.ExecutionPreviewHasChanges);
        Assert.True(snapshot.Summary.RuntimeAvailable);
        Assert.False(snapshot.Summary.PerformanceAvailable);
    }

    [Fact]
    public async Task CaptureAsync_SummaryHealthyWhenNoIssues()
    {
        SupportSnapshotProvider provider =
            CreateProvider(passed: 3, warnings: 0, failed: 0,
                hasChanges: false, prefixCount: 5,
                domainCount: 1);

        SupportSnapshot snapshot =
            await provider.CaptureAsync();

        Assert.True(snapshot.Summary.Healthy);
        Assert.False(snapshot.Summary.HasErrors);
        Assert.False(snapshot.Summary.HasWarnings);
        Assert.True(snapshot.Healthy);
        Assert.False(snapshot.HasWarnings);
        Assert.False(snapshot.HasErrors);
    }

    [Fact]
    public async Task CaptureAsync_SummaryHasErrorsWhenDiagnosticsFail()
    {
        SupportSnapshotProvider provider =
            CreateProvider(passed: 1, warnings: 0, failed: 2,
                hasChanges: false, prefixCount: 5,
                domainCount: 1);

        SupportSnapshot snapshot =
            await provider.CaptureAsync();

        Assert.False(snapshot.Summary.Healthy);
        Assert.True(snapshot.Summary.HasErrors);
        Assert.Equal(2, snapshot.Summary.DiagnosticFailedCount);
        Assert.True(snapshot.HasErrors);
    }

    [Fact]
    public async Task CaptureAsync_SummaryHasWarningsWhenDiagnosticsWarn()
    {
        SupportSnapshotProvider provider =
            CreateProvider(passed: 1, warnings: 1, failed: 0,
                hasChanges: false, prefixCount: 5,
                domainCount: 1);

        SupportSnapshot snapshot =
            await provider.CaptureAsync();

        Assert.False(snapshot.Summary.Healthy);
        Assert.False(snapshot.Summary.HasErrors);
        Assert.True(snapshot.Summary.HasWarnings);
        Assert.True(snapshot.HasWarnings);
    }

    [Fact]
    public async Task CaptureAsync_SummaryHasWarningsWhenPreviewHasChanges()
    {
        SupportSnapshotProvider provider =
            CreateProvider(passed: 1, warnings: 0, failed: 0,
                hasChanges: true, prefixCount: 5,
                domainCount: 1);

        SupportSnapshot snapshot =
            await provider.CaptureAsync();

        Assert.False(snapshot.Summary.Healthy);
        Assert.True(snapshot.Summary.HasWarnings);
    }

    [Fact]
    public async Task CaptureAsync_PrefixCountFromRuntimeSnapshot()
    {
        SupportSnapshotProvider provider =
            CreateProvider(passed: 0, warnings: 0, failed: 0,
                hasChanges: false, prefixCount: 7,
                domainCount: 0);

        SupportSnapshot snapshot =
            await provider.CaptureAsync();

        Assert.Equal(7, snapshot.Summary.PrefixCount);
    }

    [Fact]
    public async Task CaptureAsync_DnsDomainCountFromCache()
    {
        SupportSnapshotProvider provider =
            CreateProvider(passed: 0, warnings: 0, failed: 0,
                hasChanges: false, prefixCount: 0,
                domainCount: 4);

        SupportSnapshot snapshot =
            await provider.CaptureAsync();

        Assert.Equal(4, snapshot.Summary.DnsDomainCount);
    }

    [Fact]
    public async Task CaptureAsync_RuntimePrefixCountNull_DefaultsToZero()
    {
        FakeRuntimeSnapshotProvider runtime = new(
            prefixCount: null);
        SupportSnapshotProvider provider = new(
            runtime,
            new FakeDiagnosticRunner(),
            new FakePreviewPlanner(hasChanges: false),
            new FakeConfigurationService(),
            new FakePrefixMetadataService(),
            new FakePrefixHistoryService(),
            new FakeDnsCacheService(),
            timeProvider: new FixedTimeProvider(FixedTime));

        SupportSnapshot snapshot =
            await provider.CaptureAsync();

        Assert.Equal(0, snapshot.Summary.PrefixCount);
    }

    [Fact]
    public async Task CaptureAsync_PerformanceAvailableWhenPerfStoreHasReport()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "IranDirect.Tests.Support",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            RuntimePerfReportStore store = new(directory);

            RuntimeCyclePerfReport report = new()
            {
                Trigger = "repair",
                StartedAt = FixedTime,
                CompletedAt = FixedTime.AddSeconds(1),
                TotalMs = 1000,
                Categories = []
            };

            string path = store.Write(report);

            Assert.True(File.Exists(path));

            SupportSnapshotProvider provider = new(
                new FakeRuntimeSnapshotProvider(),
                new FakeDiagnosticRunner(),
                new FakePreviewPlanner(hasChanges: false),
                new FakeConfigurationService(),
                new FakePrefixMetadataService(),
                new FakePrefixHistoryService(),
                new FakeDnsCacheService(),
                perfReportStore: store,
                timeProvider: new FixedTimeProvider(FixedTime));

            SupportSnapshot snapshot =
                await provider.CaptureAsync();

            Assert.True(snapshot.Summary.PerformanceAvailable);
            Assert.NotNull(snapshot.Performance);
            Assert.Equal("repair", snapshot.Performance.Trigger);
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); }
            catch { }
        }
    }

    [Fact]
    public async Task CaptureAsync_InvokesEachDependencyExactlyOnce()
    {
        FakeRuntimeSnapshotProvider runtime = new();
        FakeDiagnosticRunner diagnostics = new(passed: 0, warnings: 0,
            failed: 0);
        FakePreviewPlanner planner = new(hasChanges: false);
        FakeConfigurationService configuration = new();
        FakePrefixMetadataService metadata =
            new(prefixCount: 0);
        FakePrefixHistoryService history = new(entries: []);
        FakeDnsCacheService dnsCache = new(domainCount: 0);

        SupportSnapshotProvider provider = new(
            runtime,
            diagnostics,
            planner,
            configuration,
            metadata,
            history,
            dnsCache,
            timeProvider: new FixedTimeProvider(FixedTime));

        SupportSnapshot snapshot =
            await provider.CaptureAsync();

        Assert.Equal(1, runtime.CallCount);
        Assert.Equal(1, diagnostics.CallCount);
        Assert.Equal(1, planner.CallCount);
        Assert.Equal(1, configuration.CallCount);
        Assert.Equal(1, metadata.GetCurrentCallCount);
        Assert.Equal(1, history.CallCount);
        Assert.Equal(1, dnsCache.CallCount);

        Assert.Same(runtime.Snapshot, snapshot.Runtime);
        Assert.Same(diagnostics.Report, snapshot.Diagnostics);
        Assert.Same(planner.Preview, snapshot.ExecutionPreview);
        Assert.Same(configuration.Configuration, snapshot.Configuration);
        Assert.Same(metadata.Metadata, snapshot.PrefixMetadata);
        Assert.Same(history.History, snapshot.PrefixHistory);
        Assert.Same(dnsCache.Statuses, snapshot.DnsCache);
    }

    [Fact]
    public async Task CaptureAsync_CancellationPropagatesFromFirstDependency()
    {
        using CancellationTokenSource cts = new();
        cts.Cancel();

        CancellingRuntimeSnapshotProvider runtime = new();

        SupportSnapshotProvider provider = new(
            runtime,
            new FakeDiagnosticRunner(),
            new FakePreviewPlanner(hasChanges: false),
            new FakeConfigurationService(),
            new FakePrefixMetadataService(),
            new FakePrefixHistoryService(),
            new FakeDnsCacheService(),
            timeProvider: new FixedTimeProvider(FixedTime));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.CaptureAsync(cts.Token));
    }

    [Fact]
    public async Task CaptureAsync_CancellationPropagatesFromDiagnosticRunner()
    {
        using CancellationTokenSource cts = new();
        cts.CancelAfter(TimeSpan.FromMilliseconds(5));

        SupportSnapshotProvider provider = new(
            new FakeRuntimeSnapshotProvider(),
            new SlowDiagnosticRunner(),
            new FakePreviewPlanner(hasChanges: false),
            new FakeConfigurationService(),
            new FakePrefixMetadataService(),
            new FakePrefixHistoryService(),
            new FakeDnsCacheService(),
            timeProvider: new FixedTimeProvider(FixedTime));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.CaptureAsync(cts.Token));
    }

    [Fact]
    public async Task CaptureAsync_RuntimeExceptionPropagates()
    {
        ThrowingRuntimeSnapshotProvider runtime = new();

        SupportSnapshotProvider provider = new(
            runtime,
            new FakeDiagnosticRunner(),
            new FakePreviewPlanner(hasChanges: false),
            new FakeConfigurationService(),
            new FakePrefixMetadataService(),
            new FakePrefixHistoryService(),
            new FakeDnsCacheService(),
            timeProvider: new FixedTimeProvider(FixedTime));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.CaptureAsync());
    }

    [Fact]
    public async Task CaptureAsync_DiagnosticExceptionPropagates()
    {
        ThrowingDiagnosticRunner diagnostics = new();

        SupportSnapshotProvider provider = new(
            new FakeRuntimeSnapshotProvider(),
            diagnostics,
            new FakePreviewPlanner(hasChanges: false),
            new FakeConfigurationService(),
            new FakePrefixMetadataService(),
            new FakePrefixHistoryService(),
            new FakeDnsCacheService(),
            timeProvider: new FixedTimeProvider(FixedTime));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.CaptureAsync());
    }

    [Fact]
    public void Snapshot_IsImmutable_PropertiesAreInitOnly()
    {
        AssertInitOnlyProperties(typeof(SupportSnapshot));
    }

    [Fact]
    public void Summary_IsImmutable_PropertiesAreInitOnly()
    {
        AssertInitOnlyProperties(typeof(SupportSnapshotSummary));
    }

    private static void AssertInitOnlyProperties(Type type)
    {
        PropertyInfo[] properties = type
            .GetProperties(
                BindingFlags.Public
                | BindingFlags.Instance)
            .Where(p => p.SetMethod is not null)
            .ToArray();

        Assert.NotEmpty(properties);

        foreach (PropertyInfo property in properties)
        {
            MethodInfo setter = property.SetMethod!;

            bool isInitOnly =
                setter.ReturnParameter
                    .GetRequiredCustomModifiers()
                    .Any(modifier =>
                        modifier.FullName ==
                            "System.Runtime.CompilerServices" +
                            ".IsExternalInit");

            Assert.True(
                isInitOnly,
                $"Property {property.Name} must be init-only.");
        }
    }

    private static SupportSnapshotProvider CreateProvider(
        int passed,
        int warnings,
        int failed,
        bool hasChanges,
        int prefixCount,
        int domainCount)
    {
        FakeRuntimeSnapshotProvider runtime =
            new(prefixCount: prefixCount);
        FakeDiagnosticRunner diagnostics = new(
            passed, warnings, failed);
        FakePreviewPlanner planner = new(hasChanges);
        FakeConfigurationService configuration = new();
        FakePrefixMetadataService metadata =
            new(prefixCount: prefixCount);
        FakePrefixHistoryService history =
            new(entries: []);
        FakeDnsCacheService dnsCache =
            new(domainCount: domainCount);

        return new SupportSnapshotProvider(
            runtime,
            diagnostics,
            planner,
            configuration,
            metadata,
            history,
            dnsCache,
            timeProvider: new FixedTimeProvider(FixedTime));
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FixedTimeProvider(DateTimeOffset now)
        {
            _now = now;
        }

        public override DateTimeOffset GetUtcNow() => _now;
    }

    private sealed class FakeRuntimeSnapshotProvider :
        IRuntimeSnapshotProvider
    {
        private readonly int? _prefixCount;
        private int _callCount;

        public FakeRuntimeSnapshotProvider(int? prefixCount = 1)
        {
            _prefixCount = prefixCount;

            Snapshot = new RuntimeSnapshot
            {
                CapturedAt = FixedTime,
                PrefixCount = prefixCount
            };
        }

        public int CallCount => _callCount;

        public RuntimeSnapshot Snapshot { get; }

        public Task<RuntimeSnapshot> GetSnapshotAsync(
            CancellationToken cancellationToken = default)
        {
            _callCount++;
            return Task.FromResult(Snapshot);
        }
    }

    private sealed class CancellingRuntimeSnapshotProvider :
        IRuntimeSnapshotProvider
    {
        public Task<RuntimeSnapshot> GetSnapshotAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new InvalidOperationException(
                "Should not be reached.");
        }
    }

    private sealed class ThrowingRuntimeSnapshotProvider :
        IRuntimeSnapshotProvider
    {
        public Task<RuntimeSnapshot> GetSnapshotAsync(
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException(
                "Runtime snapshot failed.");
        }
    }

    private sealed class FakeDiagnosticRunner :
        IDiagnosticRunner
    {
        private readonly int _passed;
        private readonly int _warnings;
        private readonly int _failed;
        private int _callCount;

        public FakeDiagnosticRunner(
            int passed = 0,
            int warnings = 0,
            int failed = 0)
        {
            _passed = passed;
            _warnings = warnings;
            _failed = failed;

            Report = new DiagnosticReport(
                FixedTime,
                BuildResults(passed, warnings, failed));
        }

        public int CallCount => _callCount;

        public DiagnosticReport Report { get; }

        public Task<DiagnosticReport> RunAllAsync(
            CancellationToken cancellationToken)
        {
            _callCount++;
            return Task.FromResult(Report);
        }

        private static IReadOnlyList<DiagnosticResult> BuildResults(
            int passed,
            int warnings,
            int failed)
        {
            List<DiagnosticResult> results = [];

            for (int i = 0; i < passed; i++)
            {
                results.Add(new DiagnosticResult(
                    Id: $"p{i}",
                    Title: $"Pass {i}",
                    Status: DiagnosticStatus.Passed,
                    Severity: DiagnosticSeverity.Pass,
                    Message: "ok",
                    SuggestedAction: null));
            }

            for (int i = 0; i < warnings; i++)
            {
                results.Add(new DiagnosticResult(
                    Id: $"w{i}",
                    Title: $"Warning {i}",
                    Status: DiagnosticStatus.Warning,
                    Severity: DiagnosticSeverity.Warning,
                    Message: "warn",
                    SuggestedAction: null));
            }

            for (int i = 0; i < failed; i++)
            {
                results.Add(new DiagnosticResult(
                    Id: $"f{i}",
                    Title: $"Failed {i}",
                    Status: DiagnosticStatus.Failed,
                    Severity: DiagnosticSeverity.Error,
                    Message: "fail",
                    SuggestedAction: null));
            }

            return results;
        }
    }

    private sealed class ThrowingDiagnosticRunner :
        IDiagnosticRunner
    {
        public Task<DiagnosticReport> RunAllAsync(
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException(
                "Diagnostic runner failed.");
        }
    }

    private sealed class SlowDiagnosticRunner :
        IDiagnosticRunner
    {
        public async Task<DiagnosticReport> RunAllAsync(
            CancellationToken cancellationToken)
        {
            await Task.Delay(
                TimeSpan.FromSeconds(5), cancellationToken);
            throw new InvalidOperationException(
                "Should not be reached.");
        }
    }

    private sealed class FakePreviewPlanner :
        IRuntimePreviewPlanner
    {
        private int _callCount;

        public FakePreviewPlanner(bool hasChanges = false)
        {
            int createCount = hasChanges ? 1 : 0;

            Preview = new ExecutionPreview
            {
                CapturedAt = FixedTime,
                Summary = new ExecutionPreviewSummary
                {
                    CreateCount = createCount,
                    DeleteCount = 0,
                    VerifyCount = 0,
                    InventoryUpdates = createCount,
                    CustomRouteUpdates = 0,
                    VpnEndpointUpdates = 0
                },
                Steps = []
            };
        }

        public int CallCount => _callCount;

        public ExecutionPreview Preview { get; }

        public Task<RuntimeDecision> BuildDecisionAsync(
            CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task<ExecutionPreview> BuildPreviewAsync(
            CancellationToken cancellationToken = default)
        {
            _callCount++;
            return Task.FromResult(Preview);
        }
    }

    private sealed class FakeConfigurationService :
        IDesiredConfigurationService
    {
        private int _callCount;

        public int CallCount => _callCount;

        public DesiredConfiguration Configuration { get; } = new();

        public Task<DesiredConfiguration> GetAsync(
            CancellationToken cancellationToken = default)
        {
            _callCount++;
            return Task.FromResult(Configuration);
        }
    }

    private sealed class FakePrefixMetadataService :
        IPrefixSourceMetadataService
    {
        private readonly int _prefixCount;
        private int _callCount;

        public FakePrefixMetadataService(int prefixCount = 0)
        {
            _prefixCount = prefixCount;

            Metadata = new PrefixSourceMetadata
            {
                SourceId = "fake",
                SourceDisplayName = "Fake",
                Format = "fake",
                ParserVersion = "1.0",
                LastAttemptedAt = FixedTime,
                LastSucceededAt = FixedTime,
                LastStatus = PrefixSourceUpdateStatus.Succeeded,
                PrefixCount = prefixCount
            };
        }

        public int GetCurrentCallCount => _callCount;

        public PrefixSourceMetadata? Metadata { get; }

        public Task<PrefixSourceMetadata?> GetCurrentAsync(
            DirectCountryCode country,
            CancellationToken cancellationToken = default)
        {
            _callCount++;
            return Task.FromResult(Metadata);
        }

        public Task<PrefixSourceChangeSummary?>
            GetLatestChangeSummaryAsync(
                DirectCountryCode country,
                CancellationToken cancellationToken = default)
        {
            return Task.FromResult<PrefixSourceChangeSummary?>(
                null);
        }

        public Task RecordSuccessAsync(
            DirectCountryCode country,
            PrefixSourceFetchResult result,
            IReadOnlyList<string>? previousPrefixes = null,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task RecordNotModifiedAsync(
            DirectCountryCode country,
            PrefixSourceFetchResult result,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task RecordFailureAsync(
            DirectCountryCode country,
            PrefixSourceDescriptor source,
            string error,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class FakePrefixHistoryService :
        IPrefixSourceUpdateHistoryService
    {
        private readonly IReadOnlyList<PrefixSourceUpdateHistoryEntry>
            _entries;
        private int _callCount;

        public FakePrefixHistoryService(
            IReadOnlyList<PrefixSourceUpdateHistoryEntry>? entries = null)
        {
            _entries = entries ?? [];
        }

        public int CallCount => _callCount;

        public IReadOnlyList<PrefixSourceUpdateHistoryEntry>
            History => _entries;

        public Task<IReadOnlyList<PrefixSourceUpdateHistoryEntry>>
            GetRecentAsync(
                DirectCountryCode country,
                int? limit = null,
                CancellationToken cancellationToken = default)
        {
            _callCount++;
            return Task.FromResult(_entries);
        }

        public Task RecordSuccessAsync(
            DirectCountryCode country,
            PrefixSourceFetchResult result,
            PrefixSourceChangeSummary? changeSummary = null,
            IReadOnlyList<string>? previousPrefixes = null,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task RecordNotModifiedAsync(
            DirectCountryCode country,
            PrefixSourceFetchResult result,
            int currentPrefixCount = 0,
            string? currentContentHash = null,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task RecordFailureAsync(
            DirectCountryCode country,
            PrefixSourceDescriptor source,
            string error,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task ClearAsync(
            DirectCountryCode country,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class FakeDnsCacheService :
        ICustomRouteDnsCacheService
    {
        private readonly int _domainCount;
        private int _callCount;

        public FakeDnsCacheService(int domainCount = 0)
        {
            _domainCount = domainCount;

            Statuses = Build(domainCount);
        }

        public int CallCount => _callCount;

        public IReadOnlyList<CustomRouteDnsCacheStatus> Statuses
            { get; }

        public Task<IReadOnlyList<CustomRouteDnsCacheStatus>>
            GetStatusAsync(
                CancellationToken cancellationToken = default)
        {
            _callCount++;
            return Task.FromResult(Statuses);
        }

        private static IReadOnlyList<CustomRouteDnsCacheStatus>
            Build(int count)
        {
            List<CustomRouteDnsCacheStatus> list = [];

            for (int i = 0; i < count; i++)
            {
                list.Add(new CustomRouteDnsCacheStatus
                {
                    CustomRouteEntryId = Guid.NewGuid(),
                    Domain = $"domain{i}.example",
                    Enabled = true,
                    State = CustomRouteDnsCacheState.Fresh,
                    IPv4Addresses = ["1.1.1.1"]
                });
            }

            return list;
        }
    }
}
