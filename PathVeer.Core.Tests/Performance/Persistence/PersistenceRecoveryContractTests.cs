namespace PathVeer.Core.Tests.Performance.Persistence;

using PathVeer.Core.Persistence;
using PathVeer.Testing.Performance.Persistence;

public sealed class PersistenceRecoveryContractTests
{
    [Fact]
    public async Task PreCancelledLoad_ThrowsAndStoreStaysUsable()
    {
        using TemporaryPersistenceWorkspace workspace =
            new("cancel-precancelled-load");
        string path = workspace.CreatePath("document.json");
        JsonStore<PersistenceEnduranceDocument> store = new(path);

        using CancellationTokenSource cts = new();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => store.LoadAsync(cts.Token));

        await store.SaveAsync(PersistenceEnduranceFixtures.Document(1));

        Assert.Equal(
            PersistenceEnduranceFixtures.Document(1),
            await store.LoadAsync());
    }

    [Fact]
    public async Task PreCancelledSave_ThrowsAndStoreStaysUsable()
    {
        using TemporaryPersistenceWorkspace workspace =
            new("cancel-precancelled-save");
        string path = workspace.CreatePath("document.json");
        JsonStore<PersistenceEnduranceDocument> store = new(path);

        using CancellationTokenSource cts = new();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => store.SaveAsync(
                PersistenceEnduranceFixtures.Document(1),
                cts.Token));

        await store.SaveAsync(PersistenceEnduranceFixtures.Document(2));

        Assert.Equal(
            PersistenceEnduranceFixtures.Document(2),
            await store.LoadAsync());
    }

    [Fact]
    public async Task CancellationWhileWaitingOnGate_ReleasesAndStoreRecovers()
    {
        using TemporaryPersistenceWorkspace workspace =
            new("cancel-while-waiting");
        string path = workspace.CreatePath("document.json");
        JsonStore<PersistenceEnduranceDocument> store = new(path);

        TaskCompletionSource entered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource hold =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        Task<PersistenceEnduranceDocument> mutation = store.MutateAsync(
            async document =>
            {
                entered.TrySetResult();
                await hold.Task;
                return PersistenceEnduranceFixtures.Document(1);
            });

        await entered.Task;

        using CancellationTokenSource cts = new();

        Task pending = store.SaveAsync(
            PersistenceEnduranceFixtures.Document(2),
            cts.Token);

        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => pending);

        hold.TrySetResult();

        PersistenceEnduranceDocument mutated = await mutation;
        Assert.Equal(PersistenceEnduranceFixtures.Document(1), mutated);

        await store.SaveAsync(PersistenceEnduranceFixtures.Document(3));

        Assert.Equal(
            PersistenceEnduranceFixtures.Document(3),
            await store.LoadAsync());

        FileSystemSnapshot snapshot = workspace.Snapshot();
        Assert.Single(snapshot.Entries);
        PersistenceEnduranceVerifier.VerifyNoOrphanTempFiles(snapshot);
    }

    [Fact]
    public async Task StaleTempFile_IsIgnoredByLoad()
    {
        using TemporaryPersistenceWorkspace workspace =
            new("stale-temp-load");
        string path = workspace.CreatePath("document.json");
        JsonStore<PersistenceEnduranceDocument> store = new(path);

        await store.SaveAsync(PersistenceEnduranceFixtures.Document(7));
        await File.WriteAllTextAsync(
            path + ".tmp",
            "stale partial garbage that is not json");

        PersistenceEnduranceDocument loaded = await store.LoadAsync();

        Assert.Equal(
            PersistenceEnduranceFixtures.Document(7),
            loaded);
    }

    [Fact]
    public async Task StaleTempFile_IsOverwrittenByNextSave_NotAppended()
    {
        using TemporaryPersistenceWorkspace workspace =
            new("stale-temp-save");
        string path = workspace.CreatePath("document.json");
        JsonStore<PersistenceEnduranceDocument> store = new(path);

        await File.WriteAllTextAsync(
            path + ".tmp-orphan",
            "stale partial garbage that is not json");

        await store.SaveAsync(PersistenceEnduranceFixtures.Document(9));

        Assert.False(File.Exists(path + ".tmp"));
        PersistenceEnduranceVerifier.VerifyFileIsCompleteJson(path);
        Assert.Equal(
            PersistenceEnduranceFixtures.Document(9),
            await store.LoadAsync());

        FileSystemSnapshot snapshot = workspace.Snapshot();
        Assert.Single(snapshot.Entries);
        PersistenceEnduranceVerifier.VerifyNoOrphanTempFiles(snapshot);
    }

    [Fact]
    public async Task CorruptFile_LoadThrowsJsonException_SubsequentSaveRepairs()
    {
        using TemporaryPersistenceWorkspace workspace =
            new("corrupt-recovery");
        string path = workspace.CreatePath("document.json");

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "{ not valid json !");

        JsonStore<PersistenceEnduranceDocument> store = new(path);

        await Assert.ThrowsAsync<System.Text.Json.JsonException>(
            () => store.LoadAsync());

        await store.SaveAsync(PersistenceEnduranceFixtures.Document(5));

        Assert.Equal(
            PersistenceEnduranceFixtures.Document(5),
            await store.LoadAsync());
        PersistenceEnduranceVerifier.VerifyFileIsCompleteJson(path);
        Assert.False(File.Exists(path + ".tmp"));
    }
}
