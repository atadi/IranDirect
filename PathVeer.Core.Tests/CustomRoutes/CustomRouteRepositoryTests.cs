using PathVeer.Core.CustomRoutes;

namespace PathVeer.Core.Tests.CustomRoutes;

public sealed class CustomRouteRepositoryTests
{
    [Fact]
    public async Task GetAllAsync_WhenFileDoesNotExist_ReturnsEmpty()
    {
        CustomRouteRepository repository = CreateRepository();

        Assert.Empty(await repository.GetAllAsync());
    }

    [Fact]
    public async Task MutateAsync_PersistsTransform()
    {
        CustomRouteRepository repository = CreateRepository();
        CustomRouteEntry entry = CreateEntry("example.com");

        await repository.MutateAsync(collection =>
            collection with
            {
                Entries = collection.Entries.Append(entry).ToArray()
            });

        CustomRouteEntry loaded =
            Assert.Single(await repository.GetAllAsync());
        Assert.Equal(entry.Id, loaded.Id);
        Assert.Equal("example.com", loaded.Value);
    }

    [Fact]
    public async Task MutateAsync_NullTransform_Throws()
    {
        CustomRouteRepository repository = CreateRepository();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => repository.MutateAsync(null!));
    }

    [Fact]
    public async Task MutateAsync_ConcurrentAppends_DoNotLoseUpdates()
    {
        CustomRouteRepository repository = CreateRepository();

        Task[] tasks = Enumerable.Range(0, 30)
            .Select(i => repository.MutateAsync(collection =>
                collection with
                {
                    Entries = collection.Entries
                        .Append(CreateEntry($"host-{i}.example.com"))
                        .ToArray()
                }))
            .ToArray();

        await Task.WhenAll(tasks);

        Assert.Equal(
            30,
            (await repository.GetAllAsync()).Count);
    }

    [Fact]
    public async Task MutateAsync_ExceptionInTransform_DoesNotPersist()
    {
        CustomRouteRepository repository = CreateRepository();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => repository.MutateAsync(collection =>
                throw new InvalidOperationException("boom")));

        Assert.Empty(await repository.GetAllAsync());
    }

    private static CustomRouteEntry CreateEntry(string value) =>
        new()
        {
            Id = Guid.NewGuid(),
            Type = CustomRouteEntryType.Domain,
            Value = value,
            Enabled = true,
            CreatedAt = DateTimeOffset.UtcNow,
            ModifiedAt = DateTimeOffset.UtcNow
        };

    private static CustomRouteRepository CreateRepository()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "IranDirect.Tests",
            Guid.NewGuid().ToString("N"),
            "custom-routes.json");

        return new CustomRouteRepository(
            new CustomRouteStore(path));
    }
}
