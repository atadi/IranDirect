namespace PathVeer.Core.Tests.Runtime.Execution;

using System.Text;
using System.Text.Json;
using PathVeer.Core.Runtime.Execution;
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

    // Audit final closure: a PRESENT but zero-length journal is truncation and
    // MUST fail closed, not be treated as an empty journal.
    [Fact]
    public async Task ZeroByteFile_ThrowsDoesNotResetToEmpty()
    {
        using TempDir dir = new();
        string path = dir.File("j.json");
        await File.WriteAllBytesAsync(path, []);

        var store = new RouteMutationJournalStore(path);

        RouteMutationJournalCorruptException ex =
            await Assert.ThrowsAsync<RouteMutationJournalCorruptException>(
                () => store.LoadAllAsync());
        Assert.Contains("zero-length", ex.Message);
    }

    // A present whitespace-only journal is NOT a legitimate empty journal.
    [Fact]
    public async Task WhitespaceOnlyFile_ThrowsDoesNotResetToEmpty()
    {
        using TempDir dir = new();
        string path = dir.File("j.json");
        await File.WriteAllTextAsync(path, "   \t  \n  ");

        var store = new RouteMutationJournalStore(path);

        RouteMutationJournalCorruptException ex =
            await Assert.ThrowsAsync<RouteMutationJournalCorruptException>(
                () => store.LoadAllAsync());
        Assert.Contains("whitespace", ex.Message);
    }

    // All-NUL bytes are not valid journal content.
    [Fact]
    public async Task AllNulFile_ThrowsDoesNotResetToEmpty()
    {
        using TempDir dir = new();
        string path = dir.File("j.json");
        await File.WriteAllBytesAsync(path, new byte[271]);

        var store = new RouteMutationJournalStore(path);

        await Assert.ThrowsAsync<RouteMutationJournalCorruptException>(
            () => store.LoadAllAsync());
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

    // A valid serialized empty journal JSON remains a valid empty journal
    // (not an error, not a reset).
    [Fact]
    public async Task ValidEmptyJournalJson_ReturnsEmpty()
    {
        using TempDir dir = new();
        string path = dir.File("j.json");
        var doc = new RouteMutationJournalFile();
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(doc));

        var store = new RouteMutationJournalStore(path);

        Assert.Empty(await store.LoadAllAsync());
    }

    // Audit final UTF-8 closure: malformed UTF-8 INSIDE a JSON string value must
    // fail closed. A lenient decoder would turn 0xC3 0x28 into U+FFFD '(' and
    // still produce syntactically valid JSON (so it would be ACCEPTED). The
    // strict decoder must reject the raw bytes instead.
    [Fact]
    public async Task InvalidUtf8InsideJsonString_FailsClosed_NotNormalized()
    {
        using TempDir dir = new();
        string path = dir.File("j.json");

        // Otherwise-valid journal JSON with a sentinel inside a string value.
        string json =
            "{\n" +
            "  \"SchemaVersion\": 1,\n" +
            "  \"Entries\": {\n" +
            "    \"pfx\": {\n" +
            "      \"Kind\": 0,\n" +
            "      \"InventoryKind\": 0,\n" +
            "      \"RouteIdentity\": \"pfx\",\n" +
            "      \"DestinationPrefix\": \"10.0.0.0/8\",\n" +
            "      \"Gateway\": \"192.168.1.1\",\n" +
            "      \"InterfaceIndex\": 12,\n" +
            "      \"Metric\": 100,\n" +
            "      \"Description\": \"MARKER\"\n" +
            "    }\n" +
            "  }\n" +
            "}";

        byte[] validBytes = System.Text.Encoding.UTF8.GetBytes(json);
        // Replace the sentinel with an invalid UTF-8 sequence: 0xC3 expects a
        // continuation byte (0x80-0xBF) but 0x28 follows.
        byte[] marker = System.Text.Encoding.UTF8.GetBytes("MARKER");
        int idx = validBytes.AsSpan().IndexOf(marker);
        Assert.True(idx >= 0);
        byte[] corruptBytes = validBytes.ToArray();
        corruptBytes[idx] = 0xC3;
        corruptBytes[idx + 1] = 0x28;

        await File.WriteAllBytesAsync(path, corruptBytes);
        byte[] before = await File.ReadAllBytesAsync(path);

        var store = new RouteMutationJournalStore(path);

        // Not normalized to U+FFFD and accepted; not silently reset to empty.
        await Assert.ThrowsAsync<RouteMutationJournalCorruptException>(
            () => store.LoadAllAsync());

        // LoadAllAsync is read-only: it must not rewrite/normalize the file.
        Assert.Equal(corruptBytes, await File.ReadAllBytesAsync(path));
        Assert.Equal(before, await File.ReadAllBytesAsync(path));
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
