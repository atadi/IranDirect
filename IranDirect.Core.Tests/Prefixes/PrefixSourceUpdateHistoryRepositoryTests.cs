using System.Text.Json;
using IranDirect.Core.Prefixes;

namespace IranDirect.Core.Tests.Prefixes;

public sealed class PrefixSourceUpdateHistoryRepositoryTests
{
    private static readonly DateTimeOffset BaseTime =
        new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task LoadAsync_WhenFileDoesNotExist_ReturnsDefault()
    {
        (PrefixSourceUpdateHistoryRepository repository, _) =
            CreateRepository();

        PrefixSourceUpdateHistoryDocument document =
            await repository.LoadAsync();

        Assert.Equal(1, document.SchemaVersion);
        Assert.Empty(document.Entries);
    }

    [Fact]
    public async Task SaveAndLoad_RoundTripsEntries()
    {
        (PrefixSourceUpdateHistoryRepository repository, _) =
            CreateRepository();

        PrefixSourceUpdateHistoryDocument expected =
            CreateDocument(
                CreateEntry(attempt: 0),
                CreateEntry(attempt: 1));

        await repository.SaveAsync(expected);

        PrefixSourceUpdateHistoryDocument actual =
            await repository.LoadAsync();

        Assert.Equal(
            expected.Entries.Select(entry => entry.Id),
            actual.Entries.Select(entry => entry.Id));
        Assert.Equal(expected.Entries[0], actual.Entries[0]);
        Assert.Equal(expected.Entries[1], actual.Entries[1]);
    }

    [Fact]
    public async Task AppendAsync_AddsEntry()
    {
        (PrefixSourceUpdateHistoryRepository repository, _) =
            CreateRepository();

        PrefixSourceUpdateHistoryEntry entry = CreateEntry();

        await repository.AppendAsync(entry);

        PrefixSourceUpdateHistoryDocument document =
            await repository.LoadAsync();

        Assert.Single(document.Entries);
        Assert.Equal(entry, document.Entries[0]);
    }

    [Fact]
    public async Task GetRecentAsync_ReturnsNewestFirst()
    {
        (PrefixSourceUpdateHistoryRepository repository, _) =
            CreateRepository();

        PrefixSourceUpdateHistoryEntry first =
            CreateEntry(attempt: 0);
        PrefixSourceUpdateHistoryEntry second =
            CreateEntry(attempt: 1);
        PrefixSourceUpdateHistoryEntry third =
            CreateEntry(attempt: 2);

        await repository.AppendAsync(first);
        await repository.AppendAsync(second);
        await repository.AppendAsync(third);

        IReadOnlyList<PrefixSourceUpdateHistoryEntry> recent =
            await repository.GetRecentAsync(10);

        Assert.Equal(
            new[] { third.Id, second.Id, first.Id },
            recent.Select(entry => entry.Id));
    }

    [Fact]
    public async Task GetRecentAsync_LimitClampsToAvailable()
    {
        (PrefixSourceUpdateHistoryRepository repository, _) =
            CreateRepository();

        await repository.AppendAsync(CreateEntry(attempt: 0));
        await repository.AppendAsync(CreateEntry(attempt: 1));
        await repository.AppendAsync(CreateEntry(attempt: 2));

        IReadOnlyList<PrefixSourceUpdateHistoryEntry> recent =
            await repository.GetRecentAsync(2);

        Assert.Equal(2, recent.Count);
        Assert.Equal(2, recent[0].AttemptedAt.Minute - BaseTime.Minute);
        Assert.Equal(1, recent[1].AttemptedAt.Minute - BaseTime.Minute);
    }

    [Fact]
    public async Task GetRecentAsync_NonPositiveLimit_ReturnsEmpty()
    {
        (PrefixSourceUpdateHistoryRepository repository, _) =
            CreateRepository();

        await repository.AppendAsync(CreateEntry(attempt: 0));

        Assert.Empty(await repository.GetRecentAsync(0));
        Assert.Empty(await repository.GetRecentAsync(-1));
    }

    [Fact]
    public async Task AppendAsync_PrunesOldestBeyondRetention()
    {
        (PrefixSourceUpdateHistoryRepository repository, _) =
            CreateRepository(retention: 3);

        for (int attempt = 0; attempt < 5; attempt++)
        {
            await repository.AppendAsync(CreateEntry(attempt: attempt));
        }

        IReadOnlyList<PrefixSourceUpdateHistoryEntry> recent =
            await repository.GetRecentAsync(10);

        Assert.Equal(3, recent.Count);
        Assert.Equal(4, recent[0].AttemptedAt.Minute - BaseTime.Minute);
        Assert.Equal(3, recent[1].AttemptedAt.Minute - BaseTime.Minute);
        Assert.Equal(2, recent[2].AttemptedAt.Minute - BaseTime.Minute);
    }

    [Fact]
    public async Task AppendAsync_RepeatedLoadsAreDeterministic()
    {
        (PrefixSourceUpdateHistoryRepository repository, _) =
            CreateRepository();

        await repository.AppendAsync(CreateEntry(attempt: 0));
        await repository.AppendAsync(CreateEntry(attempt: 1));

        PrefixSourceUpdateHistoryDocument first =
            await repository.LoadAsync();
        PrefixSourceUpdateHistoryDocument second =
            await repository.LoadAsync();

        Assert.Equal(
            first.Entries.Select(entry => entry.Id),
            second.Entries.Select(entry => entry.Id));
    }

    [Fact]
    public async Task ClearAsync_RemovesAllEntries()
    {
        (PrefixSourceUpdateHistoryRepository repository, _) =
            CreateRepository();

        await repository.AppendAsync(CreateEntry(attempt: 0));
        await repository.AppendAsync(CreateEntry(attempt: 1));

        await repository.ClearAsync();

        PrefixSourceUpdateHistoryDocument document =
            await repository.LoadAsync();

        Assert.Empty(document.Entries);
        Assert.Equal(1, document.SchemaVersion);
    }

    [Fact]
    public async Task LoadAsync_CorruptJson_Throws()
    {
        (PrefixSourceUpdateHistoryRepository repository, string path) =
            CreateRepository();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "{ not valid json !!!");

        await Assert.ThrowsAsync<JsonException>(
            () => repository.LoadAsync());
    }

    [Fact]
    public async Task LoadAsync_EmptyFile_ReturnsDefault()
    {
        (PrefixSourceUpdateHistoryRepository repository, string path) =
            CreateRepository();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "");

        PrefixSourceUpdateHistoryDocument document =
            await repository.LoadAsync();

        Assert.Empty(document.Entries);
    }

    [Fact]
    public async Task LoadAsync_MalformedPersistedEntry_Throws()
    {
        (PrefixSourceUpdateHistoryRepository repository, string path) =
            CreateRepository();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(
            path,
            """
            {
              "SchemaVersion": 1,
              "Entries": [
                {
                  "SourceId": "",
                  "SourceDisplayName": "Test",
                  "Status": "Succeeded",
                  "StartedAt": "2026-01-01T00:00:00+00:00",
                  "CompletedAt": "2026-01-01T00:00:01+00:00",
                  "AttemptedAt": "2026-01-01T00:00:00+00:00"
                }
              ]
            }
            """);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => repository.LoadAsync());
    }

    [Fact]
    public async Task SaveAsync_MalformedEntry_DoesNotPersist()
    {
        (PrefixSourceUpdateHistoryRepository repository, string path) =
            CreateRepository();

        PrefixSourceUpdateHistoryEntry invalid =
            CreateEntry() with { SourceId = "" };

        PrefixSourceUpdateHistoryDocument document =
            CreateDocument(invalid);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => repository.SaveAsync(document));

        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task AppendAsync_FailedEntryWithoutError_Throws()
    {
        (PrefixSourceUpdateHistoryRepository repository, _) =
            CreateRepository();

        PrefixSourceUpdateHistoryEntry invalid =
            CreateEntry(status: PrefixSourceUpdateStatus.Failed)
            with { Error = null };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => repository.AppendAsync(invalid));

        Assert.Empty((await repository.LoadAsync()).Entries);
    }

    [Fact]
    public async Task AppendAsync_InvalidContentHash_Throws()
    {
        (PrefixSourceUpdateHistoryRepository repository, _) =
            CreateRepository();

        PrefixSourceUpdateHistoryEntry invalid =
            CreateEntry() with { CurrentContentHash = "not-a-hash" };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => repository.AppendAsync(invalid));
    }

    [Fact]
    public async Task AppendAsync_CompletedAtEarlierThanStartedAt_Throws()
    {
        (PrefixSourceUpdateHistoryRepository repository, _) =
            CreateRepository();

        PrefixSourceUpdateHistoryEntry invalid =
            CreateEntry() with
            {
                StartedAt = BaseTime.AddMinutes(1),
                CompletedAt = BaseTime
            };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => repository.AppendAsync(invalid));
    }

    [Fact]
    public async Task ConcurrentAppends_DoNotCorruptOrLoseEntries()
    {
        (PrefixSourceUpdateHistoryRepository repository, string path) =
            CreateRepository();

        Task[] tasks = Enumerable.Range(0, 20)
            .Select(attempt => repository.AppendAsync(
                CreateEntry(attempt: attempt)))
            .ToArray();

        await Task.WhenAll(tasks);

        PrefixSourceUpdateHistoryDocument loaded =
            await repository.LoadAsync();

        Assert.Equal(20, loaded.Entries.Count);
        Assert.Equal(
            20,
            loaded.Entries.Select(entry => entry.Id).Distinct().Count());

        Assert.DoesNotContain(
            Directory.GetFiles(
                Path.GetDirectoryName(path)!),
            file => file.EndsWith(".tmp"));
    }

    private static PrefixSourceUpdateHistoryEntry CreateEntry(
        int attempt = 0,
        PrefixSourceUpdateStatus status =
            PrefixSourceUpdateStatus.Succeeded,
        Guid? id = null)
    {
        DateTimeOffset attemptedAt =
            BaseTime.AddMinutes(attempt);

        string[] prefixes =
        [
            "1.2.3.0/24",
            "10.0.0.0/8"
        ];

        return new PrefixSourceUpdateHistoryEntry
        {
            Id = id ?? Guid.NewGuid(),
            SourceId = "test-source",
            SourceDisplayName = "Test Source",
            SourceUri = "https://example.test/data.json",
            Format = "example-json",
            ParserVersion = "1",
            Status = status,
            StartedAt = attemptedAt,
            CompletedAt = attemptedAt.AddSeconds(1),
            Duration = TimeSpan.FromSeconds(1),
            AttemptedAt = attemptedAt,
            ETag = "\"etag1\"",
            PreviousContentHash = null,
            CurrentContentHash =
                PrefixContentHasher.ComputeHash(prefixes),
            ContentLength = 512,
            PrefixCount = prefixes.Length,
            AddedCount = prefixes.Length,
            RemovedCount = 0,
            UnchangedCount = 0,
            HasChanges = true,
            Error = status == PrefixSourceUpdateStatus.Failed
                ? "boom"
                : null
        };
    }

    private static PrefixSourceUpdateHistoryDocument CreateDocument(
        params PrefixSourceUpdateHistoryEntry[] entries) =>
        new()
        {
            SchemaVersion = 1,
            Entries = entries
        };

    private static (PrefixSourceUpdateHistoryRepository, string)
        CreateRepository(int retention = 100)
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "IranDirect.Tests",
            Guid.NewGuid().ToString("N"),
            "prefix-source-update-history.json");

        PrefixSourceUpdateHistoryRepository repository = new(
            new PrefixSourceUpdateHistoryStore(path),
            new PrefixSourceUpdateHistoryValidator(),
            new PrefixSourceHistoryOptions
            {
                RetentionCount = retention
            });

        return (repository, path);
    }
}
