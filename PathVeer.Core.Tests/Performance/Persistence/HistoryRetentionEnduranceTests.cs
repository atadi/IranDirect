namespace PathVeer.Core.Tests.Performance.Persistence;

using PathVeer.Core.Prefixes;
using PathVeer.Testing.Performance.Persistence;

public sealed class HistoryRetentionEnduranceTests
{
    [Fact]
    public async Task AppendOneThousandEntries_RetentionCapsDocument_NewestFirst()
    {
        using TemporaryPersistenceWorkspace workspace =
            new("history-retention");
        string path = workspace.CreatePath("history.json");

        const int retention = 25;
        PrefixSourceHistoryOptions options = new()
        {
            RetentionCount = retention
        };

        PrefixSourceUpdateHistoryStore store = new(path);
        PrefixSourceUpdateHistoryRepository repository = new(
            store,
            new PrefixSourceUpdateHistoryValidator(),
            options);

        for (int i = 0; i < 1_000; i++)
        {
            await repository.AppendAsync(
                PersistenceEnduranceFixtures.HistoryEntry(i));
        }

        PrefixSourceUpdateHistoryDocument document =
            await repository.LoadAsync();

        Assert.Equal(retention, document.Entries.Count);

        IReadOnlyList<PrefixSourceUpdateHistoryEntry> recent =
            await repository.GetRecentAsync(retention);

        Assert.Equal(retention, recent.Count);
        Assert.Equal(
            PersistenceEnduranceFixtures.DeterministicGuid(999),
            recent[0].Id);
        Assert.Equal(
            PersistenceEnduranceFixtures.DeterministicGuid(998),
            recent[1].Id);
        Assert.Equal(
            PersistenceEnduranceFixtures.DeterministicGuid(975),
            recent[^1].Id);

        IReadOnlyList<PrefixSourceUpdateHistoryEntry> limited =
            await repository.GetRecentAsync(10);

        Assert.Equal(10, limited.Count);
        Assert.Equal(
            PersistenceEnduranceFixtures.DeterministicGuid(999),
            limited[0].Id);
        Assert.Equal(
            PersistenceEnduranceFixtures.DeterministicGuid(990),
            limited[^1].Id);

        FileSystemSnapshot snapshot = workspace.Snapshot();
        Assert.Single(snapshot.Entries);
        PersistenceEnduranceVerifier.VerifyNoOrphanTempFiles(snapshot);
        PersistenceEnduranceVerifier.VerifyFileIsCompleteJson(path);
    }

    [Fact]
    public async Task DefaultRetention_IsEnforcedForLongRuns()
    {
        using TemporaryPersistenceWorkspace workspace =
            new("history-default-retention");
        string path = workspace.CreatePath("history.json");

        PrefixSourceUpdateHistoryStore store = new(path);
        PrefixSourceUpdateHistoryRepository repository = new(
            store,
            new PrefixSourceUpdateHistoryValidator());

        const int appends = 1_000;

        for (int i = 0; i < appends; i++)
        {
            await repository.AppendAsync(
                PersistenceEnduranceFixtures.HistoryEntry(i));
        }

        PrefixSourceUpdateHistoryDocument document =
            await repository.LoadAsync();

        Assert.Equal(
            new PrefixSourceHistoryOptions().RetentionCount,
            document.Entries.Count);

        IReadOnlyList<PrefixSourceUpdateHistoryEntry> recent =
            await repository.GetRecentAsync(
                new PrefixSourceHistoryOptions().RetentionCount);

        Assert.Equal(
            PersistenceEnduranceFixtures.DeterministicGuid(appends - 1),
            recent[0].Id);

        FileSystemSnapshot snapshot = workspace.Snapshot();
        Assert.Single(snapshot.Entries);
        PersistenceEnduranceVerifier.VerifyNoOrphanTempFiles(snapshot);
    }
}
