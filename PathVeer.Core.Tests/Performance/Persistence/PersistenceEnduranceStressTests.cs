namespace PathVeer.Core.Tests.Performance.Persistence;

using PathVeer.Core.Persistence;
using PathVeer.Testing.Performance.Persistence;

[Trait("Category", "Stress")]
public sealed class PersistenceEnduranceStressTests
{
    [Fact]
    public async Task JsonStore_TenThousandSequentialCycles_RoundTripsExactly()
    {
        using TemporaryPersistenceWorkspace workspace =
            new("stress-sequential-10k");
        string path = workspace.CreatePath("document.json");

        await PersistenceEnduranceRunner.RunJsonStoreAsync(path, cycles: 10_000);

        FileSystemSnapshot snapshot = workspace.Snapshot();
        Assert.Single(snapshot.Entries);
        PersistenceEnduranceVerifier.VerifyNoOrphanTempFiles(snapshot);
        PersistenceEnduranceVerifier.VerifyFileIsCompleteJson(path);
        PersistenceEnduranceVerifier.VerifyOpenableWithNoSharing(path);
    }

    [Fact]
    public async Task ConcurrentReadersWriters_TenThousandOperations_FinalStateExact()
    {
        using TemporaryPersistenceWorkspace workspace =
            new("stress-concurrent-10k");
        string path = workspace.CreatePath("document.json");
        JsonStore<PersistenceEnduranceDocument> store = new(path);

        const int writerCount = 4;
        const int perWriter = 1_250;
        const int readerCount = 4;
        const int perReader = 1_250;
        const int totalMutations = writerCount * perWriter;

        TaskCompletionSource start =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        List<Task> workers = [];

        for (int w = 0; w < writerCount; w++)
        {
            workers.Add(Task.Run(async () =>
            {
                await start.Task;

                for (int i = 0; i < perWriter; i++)
                {
                    await store.MutateAsync(document =>
                    {
                        int next = document.Revision + 1;
                        return Task.FromResult(
                            PersistenceEnduranceFixtures.Document(next));
                    });
                }
            }));
        }

        for (int r = 0; r < readerCount; r++)
        {
            workers.Add(Task.Run(async () =>
            {
                await start.Task;

                for (int i = 0; i < perReader; i++)
                {
                    PersistenceEnduranceDocument loaded =
                        await store.LoadAsync();

                    Assert.True(
                        loaded.Revision >= 0
                        && loaded.Revision <= totalMutations,
                        $"Observed an out-of-range revision {loaded.Revision}.");

                    if (loaded.Revision > 0)
                    {
                        Assert.Equal(
                            PersistenceEnduranceFixtures
                                .Document(loaded.Revision).Payload,
                            loaded.Payload);
                    }
                }
            }));
        }

        start.TrySetResult();

        Exception? failure =
            await Record.ExceptionAsync(() => Task.WhenAll(workers));

        Assert.Null(failure);

        PersistenceEnduranceDocument final = await store.LoadAsync();
        Assert.Equal(totalMutations, final.Revision);
        Assert.Equal(
            PersistenceEnduranceFixtures.Document(totalMutations).Payload,
            final.Payload);

        FileSystemSnapshot snapshot = workspace.Snapshot();
        Assert.Single(snapshot.Entries);
        PersistenceEnduranceVerifier.VerifyNoOrphanTempFiles(snapshot);
        PersistenceEnduranceVerifier.VerifyOpenableWithNoSharing(path);
    }

    [Fact]
    public async Task AllStoreMatrix_TenThousandCycles_RoundTripsExact()
    {
        using TemporaryPersistenceWorkspace workspace =
            new("stress-matrix-10k");
        const int cyclesPerStore = 1_000;

        await PersistenceEnduranceRunner.RunJsonStoreAsync(
            workspace.CreatePath("document.json"), cyclesPerStore);
        await PersistenceEnduranceRunner.RunRouteInventoryAsync(
            workspace.CreatePath("routes.json"), cyclesPerStore);
        await PersistenceEnduranceRunner.RunVpnEndpointsAsync(
            workspace.CreatePath("endpoints.json"), cyclesPerStore);
        await PersistenceEnduranceRunner.RunStateAsync(
            workspace.CreatePath("state.json"), cyclesPerStore);
        await PersistenceEnduranceRunner.RunCustomRoutesAsync(
            workspace.CreatePath("custom-routes.json"), cyclesPerStore);
        await PersistenceEnduranceRunner.RunDnsCacheAsync(
            workspace.CreatePath("dns-cache.json"), cyclesPerStore);
        await PersistenceEnduranceRunner.RunPrefixMetadataAsync(
            workspace.CreatePath("prefix-metadata.json"), cyclesPerStore);
        await PersistenceEnduranceRunner.RunDesiredConfigAsync(
            workspace.CreatePath("desired-config.json"), cyclesPerStore);
        await PersistenceEnduranceRunner.RunHistoryAsync(
            workspace.CreatePath("history.json"), cyclesPerStore);

        string perfDirectory = workspace.CreatePath("perf");
        await PersistenceEnduranceRunner.RunPerfReportsAsync(
            perfDirectory, cyclesPerStore);

        FileSystemSnapshot snapshot = workspace.Snapshot();
        Assert.Equal(
            9 + cyclesPerStore,
            snapshot.FileCount);
        PersistenceEnduranceVerifier.VerifyNoOrphanTempFiles(snapshot);

        foreach (FileSystemSnapshot.SnapshotEntry entry in snapshot.Entries)
        {
            if (!entry.RelativePath.StartsWith("perf"))
            {
                PersistenceEnduranceVerifier.VerifyFileIsCompleteJson(
                    workspace.CreatePath(entry.RelativePath));
            }
        }

        PersistenceEnduranceVerifier.VerifyOpenableWithNoSharing(
            workspace.CreatePath("routes.json"));
        PersistenceEnduranceVerifier.VerifyOpenableWithNoSharing(
            workspace.CreatePath("history.json"));
    }
}
