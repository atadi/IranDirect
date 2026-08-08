namespace PathVeer.Core.Tests.Performance.Persistence;

using PathVeer.Core.CustomRoutes;
using PathVeer.Core.Persistence;
using PathVeer.Core.Routing;
using PathVeer.Core.State;
using PathVeer.Testing.Performance.Persistence;

public sealed class PersistenceEnduranceConcurrencyTests
{
    private const int Seed = PersistenceEnduranceFixtures.DefaultSeed;

    [Fact]
    public async Task SingleInstance_ConcurrentReadersAndWriters_NoLostUpdates()
    {
        using TemporaryPersistenceWorkspace workspace =
            new("concurrent-single-instance");
        string path = workspace.CreatePath("document.json");
        JsonStore<PersistenceEnduranceDocument> store = new(path);

        const int writerCount = 4;
        const int perWriter = 250;
        const int readerCount = 4;
        const int perReader = 250;
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

                    AssertValidCommittedVersion(loaded, totalMutations);
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
    public async Task CrossInstance_ReadersAndSingleWriter_NoMalformedReads()
    {
        using TemporaryPersistenceWorkspace workspace =
            new("concurrent-cross-instance");
        string path = workspace.CreatePath("document.json");

        // Writes stay serialized through one instance (the documented
        // contract: reads and writes must never overlap on the same
        // store). Readers on separate instances must tolerate the
        // writer's atomic tmp + File.Move without any sharing violation
        // escaping the retry path.
        JsonStore<PersistenceEnduranceDocument> writer = new(path);
        const int readerCount = 4;
        const int perReader = 250;
        const int totalWrites = 1_000;

        TaskCompletionSource start =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        Task writeTask = Task.Run(async () =>
        {
            await start.Task;

            for (int i = 0; i < totalWrites; i++)
            {
                await writer.SaveAsync(
                    PersistenceEnduranceFixtures.Document(i));
            }
        });

        List<Task> readers = [];

        for (int r = 0; r < readerCount; r++)
        {
            readers.Add(Task.Run(async () =>
            {
                // A fresh store instance per reader: no shared file lock.
                JsonStore<PersistenceEnduranceDocument> reader = new(path);

                await start.Task;

                for (int i = 0; i < perReader; i++)
                {
                    PersistenceEnduranceDocument loaded =
                        await reader.LoadAsync();

                    AssertValidCommittedVersion(
                        loaded, totalWrites - 1);
                }
            }));
        }

        start.TrySetResult();

        List<Task> all = [writeTask, .. readers];

        Exception? failure =
            await Record.ExceptionAsync(() => Task.WhenAll(all));

        Assert.Null(failure);

        JsonStore<PersistenceEnduranceDocument> reader = new(path);
        PersistenceEnduranceDocument final = await reader.LoadAsync();

        // The atomic tmp + File.Move write guarantees the surviving file
        // is exactly one complete committed version.
        AssertValidCommittedVersion(final, totalWrites - 1);
        Assert.Equal(totalWrites - 1, final.Revision);

        FileSystemSnapshot snapshot = workspace.Snapshot();
        Assert.Single(snapshot.Entries);
        PersistenceEnduranceVerifier.VerifyNoOrphanTempFiles(snapshot);
        PersistenceEnduranceVerifier.VerifyOpenableWithNoSharing(path);
    }

    [Fact]
    public async Task CrossStore_DifferentTypes_SameDirectory_AreIsolated()
    {
        using TemporaryPersistenceWorkspace workspace =
            new("cross-store-isolation");
        const int cycles = 100;

        RouteInventoryStore routeStore = new(
            workspace.CreatePath("routes.json"));
        StateRepository stateStore = new(
            workspace.CreatePath("state.json"));
        CustomRouteStore customStore = new(
            workspace.CreatePath("custom-routes.json"));

        TaskCompletionSource start =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        Task routes = Task.Run(async () =>
        {
            await start.Task;

            for (int i = 0; i < cycles; i++)
            {
                await routeStore.MutateAsync(inventory => inventory with
                {
                    Routes = inventory.Routes.Append(
                        PersistenceEnduranceFixtures.RouteItem(i)).ToArray()
                });
            }
        });

        Task states = Task.Run(async () =>
        {
            await start.Task;

            for (int i = 0; i < cycles; i++)
            {
                await stateStore.SaveAsync(
                    PersistenceEnduranceFixtures.State(i));
            }
        });

        Task customs = Task.Run(async () =>
        {
            await start.Task;

            for (int i = 0; i < cycles; i++)
            {
                await customStore.SaveAsync(
                    PersistenceEnduranceFixtures.CustomRoutes(Seed, i + 1));
            }
        });

        start.TrySetResult();

        Exception? failure =
            await Record.ExceptionAsync(() => Task.WhenAll(routes, states, customs));

        Assert.Null(failure);

        RouteInventory routesLoaded = await routeStore.LoadAsync();
        Assert.Equal(
            PersistenceEnduranceFixtures.RouteInventory(Seed, cycles).Routes,
            routesLoaded.Routes);

        IranDirectState stateLoaded = await stateStore.LoadAsync();
        Assert.Equal(
            PersistenceEnduranceFixtures.State(cycles - 1),
            stateLoaded);

        CustomRouteCollection customLoaded = await customStore.LoadAsync();
        Assert.Equal(
            PersistenceEnduranceFixtures.CustomRoutes(Seed, cycles).Entries,
            customLoaded.Entries);

        FileSystemSnapshot snapshot = workspace.Snapshot();
        Assert.Equal(3, snapshot.FileCount);
        PersistenceEnduranceVerifier.VerifyNoOrphanTempFiles(snapshot);

        foreach (FileSystemSnapshot.SnapshotEntry entry in snapshot.Entries)
        {
            PersistenceEnduranceVerifier.VerifyFileIsCompleteJson(
                workspace.CreatePath(entry.RelativePath));
        }
    }

    private static void AssertValidCommittedVersion(
        PersistenceEnduranceDocument document,
        int maxRevision)
    {
        Assert.True(
            document.Revision >= 0 && document.Revision <= maxRevision,
            $"Observed an out-of-range revision {document.Revision}.");

        if (document.Revision > 0)
        {
            Assert.Equal(
                PersistenceEnduranceFixtures.Document(document.Revision).Payload,
                document.Payload);
        }
    }
}
