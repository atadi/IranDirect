using System.Text.Json;
using IranDirect.Core.CustomRoutes;

namespace IranDirect.Core.Tests.CustomRoutes;

public sealed class CustomRouteServiceTests
{
    [Fact]
    public async Task AddAsync_ValidDomain_NormalizesAndPersists()
    {
        ServiceFixture fixture = CreateFixture();
        CustomRouteEntry entry =
            await fixture.Service.AddAsync(
                CustomRouteEntryType.Domain,
                "Example.COM.",
                "my site");

        Assert.NotEqual(Guid.Empty, entry.Id);
        Assert.Equal(
            CustomRouteEntryType.Domain,
            entry.Type);
        Assert.Equal("example.com", entry.Value);
        Assert.True(entry.Enabled);
        Assert.Equal("my site", entry.Description);
        Assert.Equal(entry.CreatedAt, entry.ModifiedAt);
        Assert.Equal(fixture.Now, entry.CreatedAt);
    }

    [Fact]
    public async Task AddAsync_ValidIp_Normalizes()
    {
        ServiceFixture fixture = CreateFixture();
        CustomRouteEntry entry =
            await fixture.Service.AddAsync(
                CustomRouteEntryType.IpAddress,
                "8.8.8.8");

        Assert.Equal("8.8.8.8", entry.Value);
    }

    [Fact]
    public async Task AddAsync_ValidCidr_NormalizesHostBits()
    {
        ServiceFixture fixture = CreateFixture();
        CustomRouteEntry entry =
            await fixture.Service.AddAsync(
                CustomRouteEntryType.Cidr,
                "10.0.0.1/24");

        Assert.Equal("10.0.0.0/24", entry.Value);
    }

    [Theory]
    [InlineData(CustomRouteEntryType.Domain, "http://example.com")]
    [InlineData(CustomRouteEntryType.Domain, "*.example.com")]
    [InlineData(CustomRouteEntryType.IpAddress, "008.008.008.008")]
    [InlineData(CustomRouteEntryType.IpAddress, "::1")]
    [InlineData(CustomRouteEntryType.IpAddress, "127.0.0.1")]
    [InlineData(CustomRouteEntryType.Cidr, "10.0.0.0/0")]
    [InlineData(CustomRouteEntryType.Cidr, "10.0.0.0/33")]
    [InlineData(CustomRouteEntryType.Cidr, "::1/128")]
    [InlineData(CustomRouteEntryType.Cidr, "224.0.0.0/4")]
    [InlineData(CustomRouteEntryType.Cidr, "")]
    public async Task AddAsync_InvalidValue_ThrowsAndPersistsNothing(
        CustomRouteEntryType type,
        string value)
    {
        ServiceFixture fixture = CreateFixture();

        await Assert.ThrowsAsync<ArgumentException>(
            () => fixture.Service.AddAsync(type, value));

        Assert.Empty(await fixture.Service.GetAllAsync());
    }

    [Fact]
    public async Task AddAsync_DuplicateDomain_Throws()
    {
        ServiceFixture fixture = CreateFixture();
        await fixture.Service.AddAsync(
            CustomRouteEntryType.Domain,
            "Example.COM");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Service.AddAsync(
                CustomRouteEntryType.Domain,
                "example.com"));

        Assert.Single(await fixture.Service.GetAllAsync());
    }

    [Fact]
    public async Task AddAsync_DuplicateCidr_ThrowsAfterNormalization()
    {
        ServiceFixture fixture = CreateFixture();
        await fixture.Service.AddAsync(
            CustomRouteEntryType.Cidr,
            "10.0.0.1/24");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Service.AddAsync(
                CustomRouteEntryType.Cidr,
                "10.0.0.0/24"));

        Assert.Single(await fixture.Service.GetAllAsync());
    }

    [Fact]
    public async Task AddAsync_SameValueDifferentTypes_IsAllowed()
    {
        ServiceFixture fixture = CreateFixture();

        await fixture.Service.AddAsync(
            CustomRouteEntryType.Domain,
            "example.com");
        await fixture.Service.AddAsync(
            CustomRouteEntryType.IpAddress,
            "8.8.8.8");
        await fixture.Service.AddAsync(
            CustomRouteEntryType.Cidr,
            "10.0.0.0/24");

        Assert.Equal(
            3,
            (await fixture.Service.GetAllAsync()).Count);
    }

    [Fact]
    public async Task SetEnabledAsync_FlipsStateAndPreservesIdentity()
    {
        ServiceFixture fixture = CreateFixture();
        CustomRouteEntry added =
            await fixture.Service.AddAsync(
                CustomRouteEntryType.Domain,
                "example.com");

        fixture.Advance(TimeSpan.FromMinutes(1));

        CustomRouteEntry updated =
            await fixture.Service.SetEnabledAsync(
                added.Id,
                enabled: false);

        Assert.False(updated.Enabled);
        Assert.Equal(added.Id, updated.Id);
        Assert.Equal(added.CreatedAt, updated.CreatedAt);
        Assert.Equal(added.Value, updated.Value);
        Assert.True(updated.ModifiedAt > added.ModifiedAt);

        CustomRouteEntry reloaded =
            Assert.Single(await fixture.Service.GetAllAsync());
        Assert.False(reloaded.Enabled);
        Assert.Equal(added.Id, reloaded.Id);
        Assert.Equal(added.CreatedAt, reloaded.CreatedAt);
    }

    [Fact]
    public async Task SetEnabledAsync_MissingId_Throws()
    {
        ServiceFixture fixture = CreateFixture();

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => fixture.Service.SetEnabledAsync(
                Guid.NewGuid(),
                enabled: true));
    }

    [Fact]
    public async Task SetEnabledAsync_SameState_DoesNotTouchModifiedAt()
    {
        ServiceFixture fixture = CreateFixture();
        CustomRouteEntry added =
            await fixture.Service.AddAsync(
                CustomRouteEntryType.Domain,
                "example.com");

        fixture.Advance(TimeSpan.FromMinutes(1));

        CustomRouteEntry updated =
            await fixture.Service.SetEnabledAsync(
                added.Id,
                enabled: true);

        Assert.Equal(added.ModifiedAt, updated.ModifiedAt);
    }

    [Fact]
    public async Task UpdateAsync_Value_RevalidatesAndNormalizes()
    {
        ServiceFixture fixture = CreateFixture();
        CustomRouteEntry added =
            await fixture.Service.AddAsync(
                CustomRouteEntryType.Cidr,
                "10.0.0.1/24");

        fixture.Advance(TimeSpan.FromMinutes(1));

        CustomRouteEntry updated =
            await fixture.Service.UpdateAsync(
                added.Id,
                "192.168.10.5/16",
                description: null);

        Assert.Equal("192.168.0.0/16", updated.Value);
        Assert.Equal(added.Id, updated.Id);
        Assert.Equal(added.CreatedAt, updated.CreatedAt);
        Assert.True(updated.ModifiedAt > added.ModifiedAt);
    }

    [Fact]
    public async Task UpdateAsync_Description_SetsAndClears()
    {
        ServiceFixture fixture = CreateFixture();
        CustomRouteEntry added =
            await fixture.Service.AddAsync(
                CustomRouteEntryType.Domain,
                "example.com",
                "original");

        CustomRouteEntry withDescription =
            await fixture.Service.UpdateAsync(
                added.Id,
                value: null,
                description: "updated");

        Assert.Equal("updated", withDescription.Description);

        CustomRouteEntry cleared =
            await fixture.Service.UpdateAsync(
                added.Id,
                value: null,
                description: "");

        Assert.Equal("", cleared.Description);
    }

    [Fact]
    public async Task UpdateAsync_InvalidValue_ThrowsAndDoesNotChangeEntry()
    {
        ServiceFixture fixture = CreateFixture();
        CustomRouteEntry added =
            await fixture.Service.AddAsync(
                CustomRouteEntryType.Domain,
                "example.com");

        await Assert.ThrowsAsync<ArgumentException>(
            () => fixture.Service.UpdateAsync(
                added.Id,
                "not a domain!",
                description: null));

        CustomRouteEntry reloaded =
            Assert.Single(await fixture.Service.GetAllAsync());
        Assert.Equal("example.com", reloaded.Value);
        Assert.Equal(added.ModifiedAt, reloaded.ModifiedAt);
    }

    [Fact]
    public async Task UpdateAsync_Duplicate_Throws()
    {
        ServiceFixture fixture = CreateFixture();
        await fixture.Service.AddAsync(
            CustomRouteEntryType.Domain,
            "example.com");
        CustomRouteEntry second =
            await fixture.Service.AddAsync(
                CustomRouteEntryType.Domain,
                "example.org");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Service.UpdateAsync(
                second.Id,
                "EXAMPLE.COM",
                description: null));

        CustomRouteEntry unchanged =
            Assert.Single(
                await fixture.Service.GetAllAsync(),
                e => e.Id == second.Id);
        Assert.Equal("example.org", unchanged.Value);
    }

    [Fact]
    public async Task UpdateAsync_Unchanged_DoesNotTouchModifiedAt()
    {
        ServiceFixture fixture = CreateFixture();
        CustomRouteEntry added =
            await fixture.Service.AddAsync(
                CustomRouteEntryType.Domain,
                "example.com");

        fixture.Advance(TimeSpan.FromMinutes(1));

        CustomRouteEntry updated =
            await fixture.Service.UpdateAsync(
                added.Id,
                "example.com",
                description: null);

        Assert.Equal(added.ModifiedAt, updated.ModifiedAt);
    }

    [Fact]
    public async Task UpdateAsync_MissingId_Throws()
    {
        ServiceFixture fixture = CreateFixture();

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => fixture.Service.UpdateAsync(
                Guid.NewGuid(),
                "example.com",
                description: null));
    }

    [Fact]
    public async Task RemoveAsync_RemovesEntry()
    {
        ServiceFixture fixture = CreateFixture();
        CustomRouteEntry first =
            await fixture.Service.AddAsync(
                CustomRouteEntryType.Domain,
                "example.com");
        await fixture.Service.AddAsync(
            CustomRouteEntryType.IpAddress,
            "8.8.8.8");

        bool removed =
            await fixture.Service.RemoveAsync(first.Id);

        Assert.True(removed);

        CustomRouteEntry remaining =
            Assert.Single(await fixture.Service.GetAllAsync());
        Assert.Equal(CustomRouteEntryType.IpAddress, remaining.Type);
    }

    [Fact]
    public async Task RemoveAsync_MissingId_ReturnsFalse()
    {
        ServiceFixture fixture = CreateFixture();

        bool removed =
            await fixture.Service.RemoveAsync(Guid.NewGuid());

        Assert.False(removed);
    }

    [Fact]
    public async Task GetAllAsync_ReloadsFromDisk_StableIdsAndTimestamps()
    {
        ServiceFixture fixture = CreateFixture();
        CustomRouteEntry added =
            await fixture.Service.AddAsync(
                CustomRouteEntryType.Domain,
                "Example.COM");

        ServiceFixture reloaded = CreateFixture(fixture.Path);

        CustomRouteEntry loaded =
            Assert.Single(await reloaded.Service.GetAllAsync());

        Assert.Equal(added.Id, loaded.Id);
        Assert.Equal("example.com", loaded.Value);
        Assert.Equal(added.CreatedAt, loaded.CreatedAt);
        Assert.Equal(added.ModifiedAt, loaded.ModifiedAt);
    }

    [Fact]
    public async Task FileFormat_StoresNormalizedValuesAndStringEnums()
    {
        ServiceFixture fixture = CreateFixture();
        await fixture.Service.AddAsync(
            CustomRouteEntryType.Domain,
            "Example.COM");
        await fixture.Service.AddAsync(
            CustomRouteEntryType.Cidr,
            "10.0.0.1/24");

        string json = await File.ReadAllTextAsync(fixture.Path);

        Assert.Contains("\"SchemaVersion\": 1", json);
        Assert.Contains("\"Type\": \"Domain\"", json);
        Assert.Contains("\"Type\": \"Cidr\"", json);
        Assert.Contains("\"Value\": \"example.com\"", json);
        Assert.Contains("\"Value\": \"10.0.0.0/24\"", json);

        using JsonDocument document =
            JsonDocument.Parse(json);
        JsonElement entries =
            document.RootElement.GetProperty("Entries");
        Assert.Equal(2, entries.GetArrayLength());
    }

    [Fact]
    public async Task MissingFile_ReturnsEmptyCollection()
    {
        ServiceFixture fixture = CreateFixture();

        Assert.Empty(await fixture.Service.GetAllAsync());
    }

    [Fact]
    public async Task EmptyFile_ReturnsEmptyCollection()
    {
        ServiceFixture fixture = CreateFixture();
        Directory.CreateDirectory(
            Path.GetDirectoryName(fixture.Path)!);
        await File.WriteAllTextAsync(fixture.Path, "");

        Assert.Empty(await fixture.Service.GetAllAsync());
    }

    [Fact]
    public async Task CorruptedFile_ThrowsConsistentWithJsonStorePolicy()
    {
        ServiceFixture fixture = CreateFixture();
        Directory.CreateDirectory(
            Path.GetDirectoryName(fixture.Path)!);
        await File.WriteAllTextAsync(
            fixture.Path,
            "{ this is not valid json !!!");

        await Assert.ThrowsAsync<JsonException>(
            () => fixture.Service.GetAllAsync());
    }

    [Fact]
    public async Task ConcurrentAdditions_DoNotLoseUpdates()
    {
        ServiceFixture fixture = CreateFixture();

        Task[] tasks = Enumerable.Range(0, 50)
            .Select(i => fixture.Service.AddAsync(
                CustomRouteEntryType.Domain,
                $"host-{i}.example.com"))
            .ToArray();

        await Task.WhenAll(tasks);

        Assert.Equal(
            50,
            (await fixture.Service.GetAllAsync()).Count);
    }

    [Fact]
    public async Task ConcurrentDuplicateAdds_OnlyOneSucceeds()
    {
        ServiceFixture fixture = CreateFixture();

        Task<CustomRouteEntry>[] tasks = Enumerable.Range(0, 20)
            .Select(_ => fixture.Service.AddAsync(
                CustomRouteEntryType.Domain,
                "Example.COM"))
            .ToArray();

        int succeeded = 0;
        int duplicates = 0;

        foreach (Task<CustomRouteEntry> task in tasks)
        {
            try
            {
                await task;
                succeeded++;
            }
            catch (InvalidOperationException)
            {
                duplicates++;
            }
        }

        Assert.Equal(1, succeeded);
        Assert.Equal(19, duplicates);
        Assert.Single(await fixture.Service.GetAllAsync());
    }

    [Fact]
    public async Task ConcurrentMixedMutations_RemainConsistent()
    {
        ServiceFixture fixture = CreateFixture();
        CustomRouteEntry a =
            await fixture.Service.AddAsync(
                CustomRouteEntryType.Domain,
                "a.example.com");
        CustomRouteEntry b =
            await fixture.Service.AddAsync(
                CustomRouteEntryType.Domain,
                "b.example.com");

        Task[] tasks =
        {
            fixture.Service.SetEnabledAsync(a.Id, enabled: false),
            fixture.Service.SetEnabledAsync(b.Id, enabled: false),
            fixture.Service.UpdateAsync(
                a.Id,
                "c.example.com",
                description: "renamed"),
            fixture.Service.RemoveAsync(b.Id)
        };

        await Task.WhenAll(tasks);

        CustomRouteEntry remaining =
            Assert.Single(await fixture.Service.GetAllAsync());
        Assert.Equal(a.Id, remaining.Id);
        Assert.Equal("c.example.com", remaining.Value);
        Assert.False(remaining.Enabled);
    }

    private static ServiceFixture CreateFixture(
        string? path = null)
    {
        string storePath =
            path
            ?? Path.Combine(
                Path.GetTempPath(),
                "IranDirect.Tests",
                Guid.NewGuid().ToString("N"),
                "custom-routes.json");

        FakeTimeProvider timeProvider = new();
        CustomRouteStore store = new(storePath);
        CustomRouteRepository repository = new(store);
        CustomRouteService service = new(
            repository,
            new CustomRouteEntryValidator(),
            timeProvider);

        return new ServiceFixture
        {
            Path = storePath,
            Service = service,
            TimeProvider = timeProvider
        };
    }

    private sealed class ServiceFixture
    {
        public required string Path { get; init; }

        public required CustomRouteService Service { get; init; }

        public required FakeTimeProvider TimeProvider { get; init; }

        public DateTimeOffset Now => TimeProvider.Now;

        public void Advance(TimeSpan duration) =>
            TimeProvider.Advance(duration);
    }

    private sealed class FakeTimeProvider : TimeProvider
    {
        public DateTimeOffset Now { get; private set; } =
            new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;

        public void Advance(TimeSpan duration) =>
            Now = Now.Add(duration);
    }
}
