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