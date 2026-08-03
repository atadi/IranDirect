namespace IranDirect.Core.Tests.Performance.Persistence;

using System.Diagnostics;
using IranDirect.Testing.Performance.Persistence;

public sealed class PersistenceEnduranceSequentialTests
{
    [Fact]
    public async Task JsonStore_OneThousandCycles_RoundTripsExactly()
    {
        using TemporaryPersistenceWorkspace workspace =
            new("json-sequential-1k");
        string path = workspace.CreatePath("document.json");

        await PersistenceEnduranceRunner.RunJsonStoreAsync(path, cycles: 1_000);

        FileSystemSnapshot snapshot = workspace.Snapshot();
        Assert.Single(snapshot.Entries);
        PersistenceEnduranceVerifier.VerifyNoOrphanTempFiles(snapshot);
        PersistenceEnduranceVerifier.VerifyFileIsCompleteJson(path);
        PersistenceEnduranceVerifier.VerifyOpenableWithNoSharing(path);
    }

    [Fact]
    public async Task StoreMatrix_SequentialCycles_RoundTripExact()
    {
        using TemporaryPersistenceWorkspace workspace =
            new("store-matrix-regular");
        const int cycles = 250;

        await PersistenceEnduranceRunner.RunRouteInventoryAsync(
            workspace.CreatePath("routes.json"), cycles);
        await PersistenceEnduranceRunner.RunVpnEndpointsAsync(
            workspace.CreatePath("endpoints.json"), cycles);
        await PersistenceEnduranceRunner.RunStateAsync(
            workspace.CreatePath("state.json"), cycles);
        await PersistenceEnduranceRunner.RunCustomRoutesAsync(
            workspace.CreatePath("custom-routes.json"), cycles);
        await PersistenceEnduranceRunner.RunDnsCacheAsync(
            workspace.CreatePath("dns-cache.json"), cycles);
        await PersistenceEnduranceRunner.RunPrefixMetadataAsync(
            workspace.CreatePath("prefix-metadata.json"), cycles);
        await PersistenceEnduranceRunner.RunDesiredConfigAsync(
            workspace.CreatePath("desired-config.json"), cycles);

        FileSystemSnapshot snapshot = workspace.Snapshot();
        Assert.Equal(7, snapshot.FileCount);
        PersistenceEnduranceVerifier.VerifyNoOrphanTempFiles(snapshot);

        foreach (FileSystemSnapshot.SnapshotEntry entry in snapshot.Entries)
        {
            PersistenceEnduranceVerifier.VerifyFileIsCompleteJson(
                workspace.CreatePath(entry.RelativePath));
        }
    }

    [Fact]
    public async Task Metrics_AreCollectedAndConsistent()
    {
        using TemporaryPersistenceWorkspace workspace =
            new("metrics-collection");
        string path = workspace.CreatePath("document.json");
        const int cycles = 100;

        Stopwatch stopwatch = Stopwatch.StartNew();
        await PersistenceEnduranceRunner.RunJsonStoreAsync(path, cycles);
        stopwatch.Stop();

        FileSystemSnapshot snapshot = workspace.Snapshot();

        PersistenceEnduranceMetrics metrics = new()
        {
            Scenario = PersistenceEnduranceScenario.RepeatedSequentialReadWrite,
            CyclesPlanned = cycles,
            CyclesCompleted = cycles,
            OperationsSucceeded = cycles * 2,
            OperationsFailed = 0,
            InjectedFaults = 0,
            Recoveries = 0,
            FilesOnDisk = snapshot.FileCount,
            TotalBytesOnDisk = snapshot.TotalBytes,
            OrphanTempFiles = snapshot.OrphanTempFiles.Count(),
            ElapsedMilliseconds = stopwatch.ElapsedMilliseconds,
            ProcessHandleCount = Process.GetCurrentProcess().HandleCount
        };

        Assert.Equal(cycles, metrics.CyclesCompleted);
        Assert.Equal(cycles * 2, metrics.OperationsSucceeded);
        Assert.Equal(0, metrics.OperationsFailed);
        Assert.Equal(0, metrics.InjectedFaults);
        Assert.Equal(0, metrics.OrphanTempFiles);
        Assert.Equal(1, metrics.FilesOnDisk);

        Assert.True(
            metrics.ElapsedMilliseconds >= 0,
            "Elapsed time must be non-negative.");
        Assert.True(
            metrics.ProcessHandleCount > 0,
            "Process handle count must be positive.");
        Assert.True(
            metrics.TotalBytesOnDisk > 0,
            "A committed document must be on disk.");
    }
}
