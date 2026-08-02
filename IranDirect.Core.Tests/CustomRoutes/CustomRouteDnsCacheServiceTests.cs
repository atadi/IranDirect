using IranDirect.Core.CustomRoutes;

namespace IranDirect.Core.Tests.CustomRoutes;

public sealed class CustomRouteDnsCacheServiceTests
{
    private static readonly TimeSpan CacheDuration =
        TimeSpan.FromMinutes(30);

    private static readonly TimeSpan MaxStaleDuration =
        TimeSpan.FromHours(2);

    [Fact]
    public async Task GetStatusAsync_Fresh_HasAddressesAndFutureExpiry()
    {
        Fixture fixture = CreateFixture();
        CustomRouteEntry entry = await AddDomain(
            fixture,
            "example.com");
        await SeedSuccess(fixture, entry);

        CustomRouteDnsCacheStatus status =
            await GetSingle(fixture);

        Assert.Equal(
            CustomRouteDnsCacheState.Fresh,
            status.State);
        Assert.Equal(["8.8.8.8"], status.IPv4Addresses);
        Assert.Equal(fixture.Clock.Now, status.LastSucceededAt);
        Assert.Equal(
            fixture.Clock.Now + CacheDuration,
            status.ExpiresAt);
    }

    [Fact]
    public async Task GetStatusAsync_Stale_ExpiredAddressesButWithinStaleWindow()
    {
        Fixture fixture = CreateFixture();
        CustomRouteEntry entry = await AddDomain(
            fixture,
            "example.com");
        await SeedSuccess(fixture, entry);
        fixture.Clock.Advance(TimeSpan.FromHours(1));

        CustomRouteDnsCacheStatus status =
            await GetSingle(fixture);

        Assert.Equal(
            CustomRouteDnsCacheState.Stale,
            status.State);
        Assert.Equal(["8.8.8.8"], status.IPv4Addresses);
    }

    [Fact]
    public async Task GetStatusAsync_Expired_StaleWindowPassed()
    {
        Fixture fixture = CreateFixture();
        CustomRouteEntry entry = await AddDomain(
            fixture,
            "example.com");
        await SeedSuccess(fixture, entry);
        fixture.Clock.Advance(TimeSpan.FromHours(3));

        CustomRouteDnsCacheStatus status =
            await GetSingle(fixture);

        Assert.Equal(
            CustomRouteDnsCacheState.Expired,
            status.State);
        Assert.Equal(["8.8.8.8"], status.IPv4Addresses);
    }

    [Fact]
    public async Task GetStatusAsync_Failed_NoAddressesAndLastErrorPresent()
    {
        Fixture fixture = CreateFixture();
        CustomRouteEntry entry = await AddDomain(
            fixture,
            "broken.example");
        await fixture.CacheRepository.UpsertFailureAsync(
            entry.Id,
            "broken.example",
            "NXDOMAIN");

        CustomRouteDnsCacheStatus status =
            await GetSingle(fixture);

        Assert.Equal(
            CustomRouteDnsCacheState.Failed,
            status.State);
        Assert.Empty(status.IPv4Addresses);
        Assert.Equal("NXDOMAIN", status.LastError);
    }

    [Fact]
    public async Task GetStatusAsync_Missing_NoCacheRecord()
    {
        Fixture fixture = CreateFixture();
        await AddDomain(fixture, "example.com");

        CustomRouteDnsCacheStatus status =
            await GetSingle(fixture);

        Assert.Equal(
            CustomRouteDnsCacheState.Missing,
            status.State);
        Assert.Empty(status.IPv4Addresses);
        Assert.Null(status.LastError);
    }

    [Fact]
    public async Task GetStatusAsync_Disabled_EvenWithCacheRecord()
    {
        Fixture fixture = CreateFixture();
        CustomRouteEntry entry = await AddDomain(
            fixture,
            "example.com");
        await SeedSuccess(fixture, entry);
        await fixture.Service.SetEnabledAsync(
            entry.Id,
            enabled: false);

        CustomRouteDnsCacheStatus status =
            await GetSingle(fixture);

        Assert.Equal(
            CustomRouteDnsCacheState.Disabled,
            status.State);
        Assert.False(status.Enabled);
    }

    [Fact]
    public async Task GetStatusAsync_OrdersByDomainThenId()
    {
        Fixture fixture = CreateFixture();

        CustomRouteEntry dupSecond = new()
        {
            Id = Guid.Parse(
                "00000000-0000-0000-0000-0000000000bb"),
            Type = CustomRouteEntryType.Domain,
            Value = "dup.com",
            Enabled = true
        };

        CustomRouteEntry dupFirst = new()
        {
            Id = Guid.Parse(
                "00000000-0000-0000-0000-0000000000aa"),
            Type = CustomRouteEntryType.Domain,
            Value = "dup.com",
            Enabled = true
        };

        await fixture.Repository.MutateAsync(collection =>
            collection with
            {
                Entries =
                [
                    dupSecond,
                    dupFirst,
                    new CustomRouteEntry
                    {
                        Id = Guid.Parse(
                            "00000000-0000-0000-0000-0000000000cc"),
                        Type = CustomRouteEntryType.Domain,
                        Value = "alpha.com",
                        Enabled = true
                    }
                ]
            });

        IReadOnlyList<CustomRouteDnsCacheStatus> statuses =
            await fixture.CacheService.GetStatusAsync();

        Assert.Equal(
            ["alpha.com", "dup.com", "dup.com"],
            statuses.Select(s => s.Domain).ToArray());
        Assert.Equal(
            [
                Guid.Parse(
                    "00000000-0000-0000-0000-0000000000cc"),
                dupFirst.Id,
                dupSecond.Id
            ],
            statuses.Select(s => s.CustomRouteEntryId).ToArray());
    }

    [Fact]
    public async Task GetStatusAsync_DomainEntriesOnly()
    {
        Fixture fixture = CreateFixture();
        CustomRouteEntry domain = await AddDomain(
            fixture,
            "example.com");
        await fixture.Service.AddAsync(
            CustomRouteEntryType.IpAddress,
            "8.8.8.8");
        await fixture.Service.AddAsync(
            CustomRouteEntryType.Cidr,
            "10.0.0.0/24");

        IReadOnlyList<CustomRouteDnsCacheStatus> statuses =
            await fixture.CacheService.GetStatusAsync();

        CustomRouteDnsCacheStatus status =
            Assert.Single(statuses);
        Assert.Equal(domain.Id, status.CustomRouteEntryId);
    }

    [Fact]
    public async Task InvalidateAsync_RemovesOnlyTargetRecord()
    {
        Fixture fixture = CreateFixture();
        CustomRouteEntry first = await AddDomain(
            fixture,
            "example.com");
        CustomRouteEntry second = await AddDomain(
            fixture,
            "second.com");
        await SeedSuccess(fixture, first);
        await SeedSuccess(fixture, second);

        bool removed =
            await fixture.CacheService.InvalidateAsync(first.Id);

        Assert.True(removed);
        Assert.Null(
            await fixture.CacheRepository.GetByEntryIdAsync(
                first.Id));
        Assert.NotNull(
            await fixture.CacheRepository.GetByEntryIdAsync(
                second.Id));
    }

    [Fact]
    public async Task InvalidateAsync_WhenNoRecord_ReturnsFalse()
    {
        Fixture fixture = CreateFixture();
        CustomRouteEntry entry = await AddDomain(
            fixture,
            "example.com");

        bool removed =
            await fixture.CacheService.InvalidateAsync(entry.Id);

        Assert.False(removed);
    }

    [Fact]
    public async Task InvalidateAsync_MissingEntry_ThrowsNotFound()
    {
        Fixture fixture = CreateFixture();

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => fixture.CacheService.InvalidateAsync(
                Guid.NewGuid()));
    }

    [Fact]
    public async Task InvalidateAsync_NonDomainEntry_ThrowsNotFound()
    {
        Fixture fixture = CreateFixture();
        CustomRouteEntry entry = await fixture.Service.AddAsync(
            CustomRouteEntryType.IpAddress,
            "8.8.8.8");

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => fixture.CacheService.InvalidateAsync(entry.Id));
    }

    [Fact]
    public async Task InvalidateAllAsync_ClearsAllRecords()
    {
        Fixture fixture = CreateFixture();
        CustomRouteEntry first = await AddDomain(
            fixture,
            "example.com");
        CustomRouteEntry second = await AddDomain(
            fixture,
            "second.com");
        await SeedSuccess(fixture, first);
        await SeedSuccess(fixture, second);

        int removed =
            await fixture.CacheService.InvalidateAllAsync();

        Assert.Equal(2, removed);
        Assert.Empty(
            await fixture.CacheRepository.GetAllAsync());
    }

    [Fact]
    public async Task InvalidateAllAsync_WhenEmpty_ReturnsZero()
    {
        Fixture fixture = CreateFixture();
        await AddDomain(fixture, "example.com");

        int removed =
            await fixture.CacheService.InvalidateAllAsync();

        Assert.Equal(0, removed);
    }

    private static async Task<CustomRouteEntry> AddDomain(
        Fixture fixture,
        string domain) =>
        await fixture.Service.AddAsync(
            CustomRouteEntryType.Domain,
            domain);

    private static Task SeedSuccess(
        Fixture fixture,
        CustomRouteEntry entry) =>
        fixture.CacheRepository.UpsertSuccessAsync(
            entry.Id,
            entry.Value,
            ["8.8.8.8"],
            CacheDuration,
            MaxStaleDuration);

    private static async Task<CustomRouteDnsCacheStatus> GetSingle(
        Fixture fixture)
    {
        IReadOnlyList<CustomRouteDnsCacheStatus> statuses =
            await fixture.CacheService.GetStatusAsync();

        return Assert.Single(statuses);
    }

    private sealed class Fixture
    {
        public required CustomRouteRepository Repository { get; init; }

        public required CustomRouteService Service { get; init; }

        public required CustomRouteDnsCacheRepository CacheRepository
            { get; init; }

        public required CustomRouteDnsCacheService CacheService
            { get; init; }

        public required FakeTimeProvider Clock { get; init; }
    }

    private static Fixture CreateFixture()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "IranDirect.Tests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(directory);

        FakeTimeProvider clock = new(
            new DateTimeOffset(
                2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

        CustomRouteRepository repository = new(
            new CustomRouteStore(
                Path.Combine(
                    directory,
                    "custom-routes.json")));

        CustomRouteService service = new(
            repository,
            new CustomRouteEntryValidator(),
            clock);

        CustomRouteDnsCacheRepository cacheRepository = new(
            new CustomRouteDnsCacheStore(
                Path.Combine(
                    directory,
                    "custom-route-dns-cache.json")),
            clock);

        CustomRouteDnsCacheService cacheService = new(
            service,
            cacheRepository,
            clock);

        return new Fixture
        {
            Repository = repository,
            Service = service,
            CacheRepository = cacheRepository,
            CacheService = cacheService,
            Clock = clock
        };
    }

    private sealed class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset _now;

        public FakeTimeProvider(DateTimeOffset now)
        {
            _now = now;
        }

        public DateTimeOffset Now => _now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan duration) =>
            _now += duration;
    }
}
