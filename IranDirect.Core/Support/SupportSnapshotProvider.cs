using IranDirect.Core.Configuration;
using IranDirect.Core.CustomRoutes;
using IranDirect.Core.Diagnostics;
using IranDirect.Core.Observability;
using IranDirect.Core.Planning;
using IranDirect.Core.Prefixes;
using IranDirect.Core.Runtime.Profiling;

namespace IranDirect.Core.Support;

public sealed class SupportSnapshotProvider :
    ISupportSnapshotProvider
{
    private readonly IRuntimeSnapshotProvider _runtimeSnapshotProvider;
    private readonly IDiagnosticRunner _diagnosticRunner;
    private readonly IRuntimePreviewPlanner _previewPlanner;
    private readonly IDesiredConfigurationService _configurationService;
    private readonly IPrefixSourceMetadataService
        _prefixSourceMetadataService;
    private readonly IPrefixSourceUpdateHistoryService
        _prefixSourceUpdateHistoryService;
    private readonly ICustomRouteDnsCacheService
        _customRouteDnsCacheService;
    private readonly RuntimePerfReportStore? _perfReportStore;
    private readonly TimeProvider _timeProvider;

    public SupportSnapshotProvider(
        IRuntimeSnapshotProvider runtimeSnapshotProvider,
        IDiagnosticRunner diagnosticRunner,
        IRuntimePreviewPlanner previewPlanner,
        IDesiredConfigurationService configurationService,
        IPrefixSourceMetadataService prefixSourceMetadataService,
        IPrefixSourceUpdateHistoryService
            prefixSourceUpdateHistoryService,
        ICustomRouteDnsCacheService customRouteDnsCacheService,
        RuntimePerfReportStore? perfReportStore = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(
            runtimeSnapshotProvider);
        ArgumentNullException.ThrowIfNull(diagnosticRunner);
        ArgumentNullException.ThrowIfNull(previewPlanner);
        ArgumentNullException.ThrowIfNull(configurationService);
        ArgumentNullException.ThrowIfNull(
            prefixSourceMetadataService);
        ArgumentNullException.ThrowIfNull(
            prefixSourceUpdateHistoryService);
        ArgumentNullException.ThrowIfNull(
            customRouteDnsCacheService);

        _runtimeSnapshotProvider = runtimeSnapshotProvider;
        _diagnosticRunner = diagnosticRunner;
        _previewPlanner = previewPlanner;
        _configurationService = configurationService;
        _prefixSourceMetadataService =
            prefixSourceMetadataService;
        _prefixSourceUpdateHistoryService =
            prefixSourceUpdateHistoryService;
        _customRouteDnsCacheService =
            customRouteDnsCacheService;
        _perfReportStore = perfReportStore;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<SupportSnapshot> CaptureAsync(
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset capturedAt = _timeProvider.GetUtcNow();

        RuntimeSnapshot runtime = await _runtimeSnapshotProvider
            .GetSnapshotAsync(cancellationToken);

        DiagnosticReport diagnostics =
            await _diagnosticRunner.RunAllAsync(
                cancellationToken);

        ExecutionPreview preview =
            await _previewPlanner.BuildPreviewAsync(
                cancellationToken);

        DesiredConfiguration configuration =
            await _configurationService.GetAsync(
                cancellationToken);

        PrefixSourceMetadata? prefixMetadata =
            await _prefixSourceMetadataService.GetCurrentAsync(
                cancellationToken);

        IReadOnlyList<PrefixSourceUpdateHistoryEntry> history =
            await _prefixSourceUpdateHistoryService.GetRecentAsync(
                limit: null,
                cancellationToken: cancellationToken);

        IReadOnlyList<CustomRouteDnsCacheStatus> dnsCache =
            await _customRouteDnsCacheService.GetStatusAsync(
                cancellationToken);

        RuntimeCyclePerfReport? performance =
            _perfReportStore is null
                ? null
                : await _perfReportStore.ReadLatestAsync(
                    cancellationToken);

        SupportSnapshotSummary summary = BuildSummary(
            runtime,
            diagnostics,
            preview,
            dnsCache,
            performance);

        return new SupportSnapshot
        {
            CapturedAt = capturedAt,
            Runtime = runtime,
            Diagnostics = diagnostics,
            ExecutionPreview = preview,
            Configuration = configuration,
            PrefixMetadata = prefixMetadata,
            PrefixHistory = history,
            DnsCache = dnsCache,
            Performance = performance,
            Summary = summary
        };
    }

    private static SupportSnapshotSummary BuildSummary(
        RuntimeSnapshot runtime,
        DiagnosticReport diagnostics,
        ExecutionPreview preview,
        IReadOnlyList<CustomRouteDnsCacheStatus> dnsCache,
        RuntimeCyclePerfReport? performance)
    {
        return new SupportSnapshotSummary
        {
            DiagnosticPassedCount = diagnostics.PassedCount,
            DiagnosticWarningCount = diagnostics.WarningCount,
            DiagnosticFailedCount = diagnostics.FailedCount,
            PrefixCount = runtime.PrefixCount ?? 0,
            DnsDomainCount = dnsCache.Count,
            ExecutionPreviewHasChanges = preview.HasChanges,
            RuntimeAvailable = true,
            PerformanceAvailable = performance is not null
        };
    }
}
