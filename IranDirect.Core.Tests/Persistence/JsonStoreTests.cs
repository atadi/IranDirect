using IranDirect.Core.Persistence;

namespace IranDirect.Core.Tests.Persistence;

public sealed class JsonStoreTests
{
    [Fact]
    public async Task LoadAsync_WhenFileDoesNotExist_ReturnsDefault()
    {
        string path = CreateTemporaryPath();
        JsonStore<TestDocument> store = new(path);

        TestDocument document = await store.LoadAsync();

        Assert.NotNull(document);
        Assert.Equal("", document.Name);
        Assert.Equal(0, document.Count);
    }

    [Fact]
    public async Task SaveAndLoad_RoundTripsDocument()
    {
        string path = CreateTemporaryPath();
        JsonStore<TestDocument> store = new(path);

        await store.SaveAsync(
            new TestDocument
            {
                Name = "IranDirect",
                Count = 42
            });

        TestDocument loaded = await store.LoadAsync();

        Assert.Equal("IranDirect", loaded.Name);
        Assert.Equal(42, loaded.Count);
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public async Task LoadAsync_WhenFileIsEmpty_ReturnsDefault()
    {
        string path = CreateTemporaryPath();
        Directory.CreateDirectory(
            Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "");

        JsonStore<TestDocument> store = new(path);

        TestDocument document = await store.LoadAsync();

        Assert.Equal("", document.Name);
        Assert.Equal(0, document.Count);
    }

    [Fact]
    public async Task ConcurrentLoadAndSave_DoesNotThrowSharingViolation()
    {
        string path = CreateTemporaryPath();
        JsonStore<TestDocument> store = new(path);

        await store.SaveAsync(
            new TestDocument { Name = "start", Count = 0 });

        Task[] tasks = Enumerable.Range(0, 200)
            .Select(i => (Task)(i % 2 == 0
                ? store.LoadAsync()
                : store.SaveAsync(
                    new TestDocument { Name = $"v{i}", Count = i })))
            .ToArray();

        await Task.WhenAll(tasks);

        TestDocument loaded = await store.LoadAsync();

        Assert.NotNull(loaded);
        Assert.False(string.IsNullOrEmpty(loaded.Name));
    }

    [Fact]
    public async Task ConcurrentReadersAndWriters_DoNotOverlapReplace()
    {
        string path = CreateTemporaryPath();
        JsonStore<TestDocument> store = new(path);

        await store.SaveAsync(
            new TestDocument { Name = "base", Count = 0 });

        for (int round = 0; round < 20; round++)
        {
            Task[] readers = Enumerable.Range(0, 16)
                .Select(_ => store.LoadAsync())
                .ToArray();

            Task[] writers = Enumerable.Range(0, 16)
                .Select(i => store.SaveAsync(
                    new TestDocument { Name = $"r{round}", Count = i }))
                .ToArray();

            await Task.WhenAll(readers.Concat(writers));
        }

        TestDocument loaded = await store.LoadAsync();

        Assert.NotNull(loaded);
    }

    private static string CreateTemporaryPath()
    {
        return Path.Combine(
            Path.GetTempPath(),
            "IranDirect.Tests",
            Guid.NewGuid().ToString("N"),
            "document.json");
    }

    public sealed record TestDocument
    {
        public string Name { get; init; } = "";

        public int Count { get; init; }
    }
}