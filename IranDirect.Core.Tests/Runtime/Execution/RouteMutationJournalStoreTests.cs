namespace IranDirect.Core.Tests.Runtime.Execution;

using System.Text;
using System.Text.Json;
using IranDirect.Core.Runtime.Execution;
using Xunit;

public sealed class RouteMutationJournalStoreTests
{
    private static RouteMutationJournalEntry Sample(string identity = "203.0.113.0/24|192.168.1.1|10") =>
        new()
        {
            Kind = RouteMutationKind.Add,
            InventoryKind = RouteMutationInventoryKind.Prefix,
            RouteIdentity = identity,
            DestinationPrefix = "203.0.113.0/24",
            Gateway = "192.168.1.1",
            InterfaceIndex = 10,
            Metric = 5,
            MutationId = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow
        };

    [Fact]
    public async Task WriteThenLoad_ReturnsEntry()
    {
        using TempDir dir = new();
        var store = new RouteMutationJournalStore(dir.File("j.json"));

        await store.WriteIntentAsync(Sample());

        IReadOnlyDictionary<string, RouteMutationJournalEntry> all =
            await store.LoadAllAsync();
        Assert.Single(all);
        Assert.Equal("203.0.113.0/24|192.168.1.1|10", all.Keys.First());
    }

    [Fact]
    public async Task ClearIntent_RemovesEntry()
    {
        using TempDir dir = new();
        var store = new RouteMutationJournalStore(dir.File("j.json"));

        await store.WriteIntentAsync(Sample());
        await store.ClearIntentAsync("203.0.113.0/24|192.168.1.1|10");

        Assert.Empty(await store.LoadAllAsync());
    }

    [Fact]
    public async Task MissingFile_LoadReturnsEmpty()
    {
        using TempDir dir = new();
        var store = new RouteMutationJournalStore(dir.File("j.json"));

        Assert.Empty(await store.LoadAllAsync());
    }

    [Fact]
    public async Task CorruptContent_ThrowsDoesNotResetToEmpty()
    {
        using TempDir dir = new();
        string path = dir.File("j.json");
        await File.WriteAllTextAsync(path, "{ this is not valid json ");

        var store = new RouteMutationJournalStore(path);

        await Assert.ThrowsAsync<RouteMutationJournalCorruptException>(
            () => store.LoadAllAsync());
    }

    [Fact]
    public async Task UnsupportedSchemaVersion_Throws()
    {
        using TempDir dir = new();
        string path = dir.File("j.json");
        var doc = new RouteMutationJournalFile { SchemaVersion = 999 };
        await File.WriteAllTextAsync(path,
            JsonSerializer.Serialize(doc));

        var store = new RouteMutationJournalStore(path);

        RouteMutationJournalCorruptException ex =
            await Assert.ThrowsAsync<RouteMutationJournalCorruptException>(
                () => store.LoadAllAsync());
        Assert.Contains("999", ex.Message);
    }

    [Fact]
    public async Task WriteIntent_ReplacesExistingForSameIdentity()
    {
        using TempDir dir = new();
        var store = new RouteMutationJournalStore(dir.File("j.json"));

        await store.WriteIntentAsync(Sample());
        await store.WriteIntentAsync(Sample() with
        {
            Kind = RouteMutationKind.Delete
        });

        IReadOnlyDictionary<string, RouteMutationJournalEntry> all =
            await store.LoadAllAsync();
        Assert.Single(all);
        Assert.Equal(RouteMutationKind.Delete, all.Values.First().Kind);
    }

    private sealed class TempDir : IDisposable
    {
        public string Path { get; } =
            System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "irandirect-journal-" + Guid.NewGuid().ToString("N"));

        public TempDir() => Directory.CreateDirectory(Path);

        public string File(string name) =>
            System.IO.Path.Combine(Path, name);

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch { }
        }
    }
}
