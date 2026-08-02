using IranDirect.Core.Configuration;
using IranDirect.Core.Diagnostics;
using IranDirect.Core.Diagnostics.Runtime;
using IranDirect.Core.Observability;
using IranDirect.Core.Runtime.Execution;
using IranDirect.Core.Runtime.Profiling;

namespace IranDirect.Core.Tests.Diagnostics.Runtime;

public sealed class RuntimeSnapshotDiagnosticCheckTests
{
    private sealed class FakeSnapshotProvider :
        IRuntimeSnapshotProvider
    {
        private readonly RuntimeSnapshot _snapshot = null!;
        private readonly Exception? _exception;

        public FakeSnapshotProvider(RuntimeSnapshot snapshot)
        {
            _snapshot = snapshot;
        }

        public FakeSnapshotProvider(Exception exception)
        {
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

    private static RuntimeSnapshot ValidSnapshot() =>
        new()
        {
            CapturedAt = DateTimeOffset.UtcNow,
            SchemaVersion = 1,
            Runtime = new IranDirectStatus
            {
                Enabled = false,
                DesiredEnabled = false
            },
            Configuration = new DesiredConfiguration
            {
                SchemaVersion = 1,
                VpnProvider = VpnProviderType.OpenVpn,
                VpnProfilePath = "test.ovpn",
                RepairInterval = TimeSpan.FromSeconds(30),
                PrefixUpdateInterval = TimeSpan.FromDays(1)
            },
            InstalledRouteCount = 0,
            PrefixCount = 0,
            DnsCache = []
        };

    [Fact]
    public async Task CheckAsync_ValidSnapshot_ReturnsPassed()
    {
        var check = new RuntimeSnapshotDiagnosticCheck(
            new FakeSnapshotProvider(ValidSnapshot()));

        DiagnosticResult result = await check.CheckAsync(
            CancellationToken.None);

        Assert.Equal(DiagnosticStatus.Passed, result.Status);
        Assert.Equal(DiagnosticSeverity.Info, result.Severity);
    }

    [Fact]
    public async Task CheckAsync_DefaultCapturedAt_ReturnsFailed()
    {
        RuntimeSnapshot snapshot =
            ValidSnapshot() with { CapturedAt = default };

        var check = new RuntimeSnapshotDiagnosticCheck(
            new FakeSnapshotProvider(snapshot));

        DiagnosticResult result = await check.CheckAsync(
            CancellationToken.None);

        Assert.Equal(DiagnosticStatus.Failed, result.Status);
        Assert.Contains("CapturedAt", result.Message);
    }

    [Fact]
    public async Task CheckAsync_UnsupportedSchema_ReturnsFailed()
    {
        RuntimeSnapshot snapshot =
            ValidSnapshot() with { SchemaVersion = 2 };

        var check = new RuntimeSnapshotDiagnosticCheck(
            new FakeSnapshotProvider(snapshot));

        DiagnosticResult result = await check.CheckAsync(
            CancellationToken.None);

        Assert.Equal(DiagnosticStatus.Failed, result.Status);
        Assert.Contains("schema version", result.Message);
    }

    [Fact]
    public async Task CheckAsync_NullRuntime_ReturnsFailed()
    {
        RuntimeSnapshot snapshot =
            ValidSnapshot() with { Runtime = null };

        var check = new RuntimeSnapshotDiagnosticCheck(
            new FakeSnapshotProvider(snapshot));

        DiagnosticResult result = await check.CheckAsync(
            CancellationToken.None);

        Assert.Equal(DiagnosticStatus.Failed, result.Status);
        Assert.Contains("Runtime", result.Message);
    }

    [Fact]
    public async Task CheckAsync_NullConfiguration_ReturnsFailed()
    {
        RuntimeSnapshot snapshot =
            ValidSnapshot() with { Configuration = null };

        var check = new RuntimeSnapshotDiagnosticCheck(
            new FakeSnapshotProvider(snapshot));

        DiagnosticResult result = await check.CheckAsync(
            CancellationToken.None);

        Assert.Equal(DiagnosticStatus.Failed, result.Status);
        Assert.Contains("Configuration", result.Message);
    }

    [Fact]
    public async Task CheckAsync_NegativeRouteCount_ReturnsFailed()
    {
        RuntimeSnapshot snapshot =
            ValidSnapshot() with { InstalledRouteCount = -1 };

        var check = new RuntimeSnapshotDiagnosticCheck(
            new FakeSnapshotProvider(snapshot));

        DiagnosticResult result = await check.CheckAsync(
            CancellationToken.None);

        Assert.Equal(DiagnosticStatus.Failed, result.Status);
        Assert.Contains("InstalledRouteCount", result.Message);
    }

    [Fact]
    public async Task CheckAsync_NegativePrefixCount_ReturnsFailed()
    {
        RuntimeSnapshot snapshot =
            ValidSnapshot() with { PrefixCount = -1 };

        var check = new RuntimeSnapshotDiagnosticCheck(
            new FakeSnapshotProvider(snapshot));

        DiagnosticResult result = await check.CheckAsync(
            CancellationToken.None);

        Assert.Equal(DiagnosticStatus.Failed, result.Status);
        Assert.Contains("PrefixCount", result.Message);
    }

    [Fact]
    public async Task CheckAsync_NullDnsCache_ReturnsFailed()
    {
        RuntimeSnapshot snapshot =
            ValidSnapshot() with { DnsCache = null! };

        var check = new RuntimeSnapshotDiagnosticCheck(
            new FakeSnapshotProvider(snapshot));

        DiagnosticResult result = await check.CheckAsync(
            CancellationToken.None);

        Assert.Equal(DiagnosticStatus.Failed, result.Status);
        Assert.Contains("DnsCache", result.Message);
    }

    [Fact]
    public async Task CheckAsync_NegativeTotalMs_ReturnsFailed()
    {
        RuntimeSnapshot snapshot = ValidSnapshot() with
        {
            Performance = new RuntimeCyclePerfReport
            {
                Trigger = "test",
                StartedAt = DateTimeOffset.UtcNow,
                CompletedAt = DateTimeOffset.UtcNow,
                TotalMs = -1,
                Categories = [],
                PlannedSteps = 1,
                CompletedSteps = 1
            }
        };

        var check = new RuntimeSnapshotDiagnosticCheck(
            new FakeSnapshotProvider(snapshot));

        DiagnosticResult result = await check.CheckAsync(
            CancellationToken.None);

        Assert.Equal(DiagnosticStatus.Failed, result.Status);
        Assert.Contains("TotalMs", result.Message);
    }

    [Fact]
    public async Task CheckAsync_NullPerformance_ReturnsPassed()
    {
        RuntimeSnapshot snapshot =
            ValidSnapshot() with { Performance = null };

        var check = new RuntimeSnapshotDiagnosticCheck(
            new FakeSnapshotProvider(snapshot));

        DiagnosticResult result = await check.CheckAsync(
            CancellationToken.None);

        Assert.Equal(DiagnosticStatus.Passed, result.Status);
    }

    [Fact]
    public async Task CheckAsync_ThrowsException_ReturnsFailed()
    {
        var check = new RuntimeSnapshotDiagnosticCheck(
            new FakeSnapshotProvider(
                new InvalidOperationException("no snapshot")));

        DiagnosticResult result = await check.CheckAsync(
            CancellationToken.None);

        Assert.Equal(DiagnosticStatus.Failed, result.Status);
        Assert.Equal(DiagnosticSeverity.Error, result.Severity);
        Assert.NotNull(result.SuggestedAction);
    }
}
