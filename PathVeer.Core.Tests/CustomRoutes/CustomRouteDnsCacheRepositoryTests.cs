using System.Text.Json;
using PathVeer.Core.CustomRoutes;
using PathVeer.Core.Persistence;

namespace PathVeer.Core.Tests.CustomRoutes;

public sealed class CustomRouteDnsCacheRepositoryTests
{
    private static readonly DateTimeOffset Start =
        new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan CacheDuration =
        TimeSpan.FromMinutes(30);

    private static readonly TimeSpan StaleDuration =
        TimeSpan.FromHours(2);

    [Fact]
    public async Task GetAllAsync_WhenFileDoesNotExist_ReturnsEmpty()
    {
        CustomRouteDnsCacheRepository repository = CreateRepository();

        Assert.Empty(await repository.GetAllAsync());
    }

    [Fact]
    public async Task GetAllAsync_WhenFileIsCorruptAndNoBackup_FailsClearly()
    {
        string path = CreatePath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "{ this is not valid json");

        CustomRouteDnsCacheRepository repository =
            new(
                new CustomRouteDnsCacheStore(path),
                new FakeTimeProvider(Start));

        // Derived cache now uses backup rollback: with no valid backup, a
        // corrupt primary fails clearly instead of silently returning empty.
        await Assert.ThrowsAsync<PersistenceCorruptException>(
            () => repository.GetAllAsync());
    }

    [Fact]
    public async Task GetAllAsync_WhenFileIsCorruptButBackupValid_Recovers()
    {
        string path = CreatePath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        CustomRouteDnsCacheStore seed = new(path);
        await seed.SaveAsync(
            new CustomRouteDnsCacheCollection
            {
                Entries =
                [
                    new CustomRouteDnsCacheEntry
                    {
                        CustomRouteEntryId = Guid.NewGuid(),
                        Domain = "example.com",
                        IPv4Addresses = ["8.8.8.8"],
                        LastAttemptedAt = Start
                    }
                ]
            });
        await seed.SaveAsync(
            new CustomRouteDnsCacheCollection
            {
                Entries =
                [
                    new CustomRouteDnsCacheEntry
                    {
                        CustomRouteEntryId = Guid.NewGuid(),
                        Domain = "good.example",
                        IPv4Addresses = ["1.1.1.1"],
                        LastAttemptedAt = Start
                    }
                ]
            });
        // primary = good.example, .bak = example.com.

        // Corrupt the primary (all NUL) to mimic the historical incident.
        await File.WriteAllBytesAsync(path, new byte[44]);

        CustomRouteDnsCacheRepository repository =
            new(new CustomRouteDnsCacheStore(path), new FakeTimeProvider(Start));

        IReadOnlyList<CustomRouteDnsCacheEntry> entries =
            await repository.GetAllAsync();

        // Recovered from the last known-good backup (example.com), not the
        // corrupt primary.
        CustomRouteDnsCacheEntry recovered = Assert.Single(entries);
        Assert.Equal("example.com", recovered.Domain);
        Assert.True(File.Exists(path + ".corrupt"));
    }

    [Fact]
    public async Task UpsertSuccessAsync_InsertsEntry()
    {
        CustomRouteDnsCacheRepository repository = CreateRepository();
        Guid entryId = Guid.NewGuid();

        await repository.UpsertSuccessAsync(
            entryId,
            "example.com",
            ["8.8.8.8", "8.8.4.4"],
            CacheDuration,
            StaleDuration);

        CustomRouteDnsCacheEntry entry =
            Assert.Single(await repository.GetAllAsync());

        Assert.Equal(entryId, entry.CustomRouteEntryId);
        Assert.Equal("example.com", entry.Domain);
        Assert.Equal(["8.8.4.4", "8.8.8.8"], entry.IPv4Addresses);
        Assert.Equal(Start, entry.LastAttemptedAt);
        Assert.Equal(Start, entry.LastSucceededAt);
        Assert.Equal(Start + CacheDuration, entry.ExpiresAt);
        Assert.Equal(Start + StaleDuration, entry.StaleUntil);
        Assert.Null(entry.LastError);
    }

    [Fact]
    public async Task UpsertSuccessAsync_ReplacesExistingEntry()
    {
        FakeTimeProvider clock = new(Start);
        CustomRouteDnsCacheRepository repository =
            CreateRepository(timeProvider: clock);
        Guid entryId = Guid.NewGuid();

        await repository.UpsertSuccessAsync(
            entryId,
            "example.com",
            ["8.8.8.8"],
            CacheDuration,
            StaleDuration);

        clock.Advance(TimeSpan.FromHours(1));

        await repository.UpsertSuccessAsync(
            entryId,
            "example.com",
            ["9.9.9.9", "1.1.1.1"],
            TimeSpan.FromMinutes(15),
            TimeSpan.FromHours(1));

        CustomRouteDnsCacheEntry entry =
            Assert.Single(await repository.GetAllAsync());

        Assert.Equal(["1.1.1.1", "9.9.9.9"], entry.IPv4Addresses);
        Assert.Equal(Start + TimeSpan.FromHours(1), entry.LastAttemptedAt);
        Assert.Equal(Start + TimeSpan.FromHours(1), entry.LastSucceededAt);
        Assert.Equal(
            Start + TimeSpan.FromHours(1) + TimeSpan.FromMinutes(15),
            entry.ExpiresAt);
        Assert.Equal(
            Start + TimeSpan.FromHours(2),
            entry.StaleUntil);
    }

    [Fact]
    public async Task UpsertSuccessAsync_NormalizesDomainToLowercase()
    {
        CustomRouteDnsCacheRepository repository = CreateRepository();

        await repository.UpsertSuccessAsync(
            Guid.NewGuid(),
            "Example.COM.",
            ["8.8.8.8"],
            CacheDuration,
            StaleDuration);

        CustomRouteDnsCacheEntry entry =
            Assert.Single(await repository.GetAllAsync());

        Assert.Equal("example.com", entry.Domain);
    }

    [Fact]
    public async Task UpsertSuccessAsync_DeduplicatesAndSortsAddresses()
    {
        CustomRouteDnsCacheRepository repository = CreateRepository();

        await repository.UpsertSuccessAsync(
            Guid.NewGuid(),
            "example.com",
            ["8.8.8.8", "1.1.1.1", " 8.8.8.8 ", "8.8.4.4", "8.8.8.8"],
            CacheDuration,
            StaleDuration);

        CustomRouteDnsCacheEntry entry =
            Assert.Single(await repository.GetAllAsync());

        Assert.Equal(
            ["1.1.1.1", "8.8.4.4", "8.8.8.8"],
            entry.IPv4Addresses);
    }

    [Fact]
    public async Task UpsertSuccessAsync_ClearsPreviousError()
    {
        CustomRouteDnsCacheRepository repository = CreateRepository();
        Guid entryId = Guid.NewGuid();

        await repository.UpsertFailureAsync(
            entryId,
            "example.com",
            "DNS resolution failed");

        await repository.UpsertSuccessAsync(
            entryId,
            "example.com",
            ["8.8.8.8"],
            CacheDuration,
            StaleDuration);

        CustomRouteDnsCacheEntry entry =
            Assert.Single(await repository.GetAllAsync());

        Assert.Equal(["8.8.8.8"], entry.IPv4Addresses);
        Assert.Null(entry.LastError);
        Assert.Equal(Start, entry.LastSucceededAt);
    }

    [Fact]
    public async Task UpsertSuccessAsync_PersistsAcrossRepositoryInstances()
    {
        CustomRouteDnsCacheStore store = CreateStore();
        Guid entryId = Guid.NewGuid();

        await CreateRepository(store).UpsertSuccessAsync(
            entryId,
            "example.com",
            ["8.8.8.8", "8.8.4.4"],
            CacheDuration,
            StaleDuration);

        CustomRouteDnsCacheEntry entry =
            Assert.Single(await CreateRepository(store).GetAllAsync());

        Assert.Equal(entryId, entry.CustomRouteEntryId);
        Assert.Equal("example.com", entry.Domain);
        Assert.Equal(["8.8.4.4", "8.8.8.8"], entry.IPv4Addresses);
        Assert.Equal(Start + CacheDuration, entry.ExpiresAt);
    }

    [Fact]
    public async Task UpsertFailureAsync_WithPriorSuccess_PreservesKnownGood()
    {
        FakeTimeProvider clock = new(Start);
        CustomRouteDnsCacheRepository repository =
            CreateRepository(timeProvider: clock);
        Guid entryId = Guid.NewGuid();

        await repository.UpsertSuccessAsync(
            entryId,
            "example.com",
            ["8.8.8.8"],
            CacheDuration,
            StaleDuration);

        clock.Advance(TimeSpan.FromHours(1));

        await repository.UpsertFailureAsync(
            entryId,
            "example.com",
            "DNS resolution failed: timeout");

        CustomRouteDnsCacheEntry entry =
            Assert.Single(await repository.GetAllAsync());

        Assert.Equal(["8.8.8.8"], entry.IPv4Addresses);
        Assert.Equal(Start, entry.LastSucceededAt);
        Assert.Equal(Start + CacheDuration, entry.ExpiresAt);
        Assert.Equal(Start + StaleDuration, entry.StaleUntil);
        Assert.Equal(Start + TimeSpan.FromHours(1), entry.LastAttemptedAt);
        Assert.Equal("DNS resolution failed: timeout", entry.LastError);
    }

    [Fact]
    public async Task UpsertFailureAsync_WithoutPriorSuccess_CreatesMinimalEntry()
    {
        CustomRouteDnsCacheRepository repository = CreateRepository();
        Guid entryId = Guid.NewGuid();

        await repository.UpsertFailureAsync(
            entryId,
            "example.com",
            "DNS resolution failed");

        CustomRouteDnsCacheEntry entry =
            Assert.Single(await repository.GetAllAsync());

        Assert.Equal(entryId, entry.CustomRouteEntryId);
        Assert.Equal("example.com", entry.Domain);
        Assert.Empty(entry.IPv4Addresses);
        Assert.Equal(Start, entry.LastAttemptedAt);
        Assert.Equal("DNS resolution failed", entry.LastError);
        Assert.Null(entry.LastSucceededAt);
        Assert.Null(entry.ExpiresAt);
        Assert.Null(entry.StaleUntil);
    }

    [Fact]
    public async Task UpsertFailureAsync_CollapsesNewlinesAndTruncatesError()
    {
        CustomRouteDnsCacheRepository repository = CreateRepository();

        string longLine = string.Concat(
            Enumerable.Repeat("very long line ", 200));

        await repository.UpsertFailureAsync(
            Guid.NewGuid(),
            "example.com",
            $"Resolution failed\n    at Somewhere\n    at Elsewhere\n{longLine}");

        CustomRouteDnsCacheEntry entry =
            Assert.Single(await repository.GetAllAsync());

        Assert.NotNull(entry.LastError);
        Assert.Equal(512, entry.LastError.Length);
        Assert.StartsWith(
            "Resolution failed at Somewhere at Elsewhere",
            entry.LastError);
        Assert.DoesNotContain('\n', entry.LastError);
        Assert.DoesNotContain('\r', entry.LastError);
    }

    [Fact]
    public async Task GetByEntryIdAsync_ReturnsMatchingEntry()
    {
        CustomRouteDnsCacheRepository repository = CreateRepository();
        Guid firstId = Guid.NewGuid();
        Guid secondId = Guid.NewGuid();

        await repository.UpsertSuccessAsync(
            firstId,
            "first.example.com",
            ["1.1.1.1"],
            CacheDuration,
            StaleDuration);

        await repository.UpsertSuccessAsync(
            secondId,
            "second.example.com",
            ["2.2.2.2"],
            CacheDuration,
            StaleDuration);

        CustomRouteDnsCacheEntry? entry =
            await repository.GetByEntryIdAsync(firstId);

        Assert.NotNull(entry);
        Assert.Equal(firstId, entry.CustomRouteEntryId);
        Assert.Equal("first.example.com", entry.Domain);
    }

    [Fact]
    public async Task GetByEntryIdAsync_WhenMissing_ReturnsNull()
    {
        CustomRouteDnsCacheRepository repository = CreateRepository();

        Assert.Null(
            await repository.GetByEntryIdAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task RemoveAsync_RemovesExistingEntry()
    {
        CustomRouteDnsCacheRepository repository = CreateRepository();
        Guid removedId = Guid.NewGuid();
        Guid keptId = Guid.NewGuid();

        await repository.UpsertSuccessAsync(
            removedId,
            "removed.example.com",
            ["1.1.1.1"],
            CacheDuration,
            StaleDuration);

        await repository.UpsertSuccessAsync(
            keptId,
            "kept.example.com",
            ["2.2.2.2"],
            CacheDuration,
            StaleDuration);

        bool removed = await repository.RemoveAsync(removedId);

        Assert.True(removed);

        CustomRouteDnsCacheEntry entry =
            Assert.Single(await repository.GetAllAsync());

        Assert.Equal(keptId, entry.CustomRouteEntryId);
    }

    [Fact]
    public async Task RemoveAsync_WhenMissing_ReturnsFalse()
    {
        CustomRouteDnsCacheRepository repository = CreateRepository();

        Assert.False(await repository.RemoveAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task RemoveMissingEntriesAsync_RemovesOnlyMissingEntries()
    {
        CustomRouteDnsCacheRepository repository = CreateRepository();
        Guid firstId = Guid.NewGuid();
        Guid secondId = Guid.NewGuid();
        Guid thirdId = Guid.NewGuid();

        await repository.UpsertSuccessAsync(
            firstId,
            "first.example.com",
            ["1.1.1.1"],
            CacheDuration,
            StaleDuration);

        await repository.UpsertSuccessAsync(
            secondId,
            "second.example.com",
            ["2.2.2.2"],
            CacheDuration,
            StaleDuration);

        await repository.UpsertSuccessAsync(
            thirdId,
            "third.example.com",
            ["3.3.3.3"],
            CacheDuration,
            StaleDuration);

        int removed = await repository.RemoveMissingEntriesAsync(
            [firstId, secondId]);

        Assert.Equal(1, removed);

        IReadOnlyList<CustomRouteDnsCacheEntry> remaining =
            await repository.GetAllAsync();

        Assert.Equal(2, remaining.Count);
        Assert.Contains(remaining, e => e.CustomRouteEntryId == firstId);
        Assert.Contains(remaining, e => e.CustomRouteEntryId == secondId);
    }

    [Fact]
    public async Task ConcurrentDifferentEntryUpserts_DoNotLoseRecords()
    {
        CustomRouteDnsCacheRepository repository = CreateRepository();

        Task[] tasks = Enumerable.Range(0, 25)
            .Select(i => repository.UpsertSuccessAsync(
                Guid.NewGuid(),
                $"host-{i}.example.com",
                [$"1.1.1.{i % 250 + 1}"],
                CacheDuration,
                StaleDuration))
            .ToArray();

        await Task.WhenAll(tasks);

        Assert.Equal(25, (await repository.GetAllAsync()).Count);
    }

    [Fact]
    public async Task ConcurrentSameEntryUpserts_LeaveOneValidRecord()
    {
        CustomRouteDnsCacheRepository repository = CreateRepository();
        Guid entryId = Guid.NewGuid();

        Task[] tasks = Enumerable.Range(0, 20)
            .Select(_ => repository.UpsertSuccessAsync(
                entryId,
                "example.com",
                ["8.8.8.8", "1.1.1.1"],
                CacheDuration,
                StaleDuration))
            .ToArray();

        await Task.WhenAll(tasks);

        Assert.Single(await repository.GetAllAsync());
    }

    [Fact]
    public async Task UpsertSuccessAsync_EmptyEntryId_Throws()
    {
        CustomRouteDnsCacheRepository repository = CreateRepository();

        await Assert.ThrowsAsync<ArgumentException>(
            () => repository.UpsertSuccessAsync(
                Guid.Empty,
                "example.com",
                ["8.8.8.8"],
                CacheDuration,
                StaleDuration));
    }

    [Fact]
    public async Task UpsertSuccessAsync_EmptyDomain_Throws()
    {
        CustomRouteDnsCacheRepository repository = CreateRepository();

        await Assert.ThrowsAsync<ArgumentException>(
            () => repository.UpsertSuccessAsync(
                Guid.NewGuid(),
                "",
                ["8.8.8.8"],
                CacheDuration,
                StaleDuration));
    }

    [Fact]
    public async Task UpsertSuccessAsync_InvalidDomain_Throws()
    {
        CustomRouteDnsCacheRepository repository = CreateRepository();

        await Assert.ThrowsAsync<ArgumentException>(
            () => repository.UpsertSuccessAsync(
                Guid.NewGuid(),
                "not a domain",
                ["8.8.8.8"],
                CacheDuration,
                StaleDuration));
    }

    [Fact]
    public async Task UpsertSuccessAsync_InvalidAddress_Throws()
    {
        CustomRouteDnsCacheRepository repository = CreateRepository();

        await Assert.ThrowsAsync<ArgumentException>(
            () => repository.UpsertSuccessAsync(
                Guid.NewGuid(),
                "example.com",
                ["300.1.1.1"],
                CacheDuration,
                StaleDuration));
    }

    [Fact]
    public async Task UpsertSuccessAsync_MalformedIpv4Address_Throws()
    {
        CustomRouteDnsCacheRepository repository = CreateRepository();

        await Assert.ThrowsAsync<ArgumentException>(
            () => repository.UpsertSuccessAsync(
                Guid.NewGuid(),
                "example.com",
                ["1.2.3"],
                CacheDuration,
                StaleDuration));
    }

    [Fact]
    public async Task UpsertSuccessAsync_Ipv6Address_Throws()
    {
        CustomRouteDnsCacheRepository repository = CreateRepository();

        await Assert.ThrowsAsync<ArgumentException>(
            () => repository.UpsertSuccessAsync(
                Guid.NewGuid(),
                "example.com",
                ["2001:db8::1"],
                CacheDuration,
                StaleDuration));
    }

    [Fact]
    public async Task UpsertSuccessAsync_ZeroCacheDuration_Throws()
    {
        CustomRouteDnsCacheRepository repository = CreateRepository();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => repository.UpsertSuccessAsync(
                Guid.NewGuid(),
                "example.com",
                ["8.8.8.8"],
                TimeSpan.Zero,
                StaleDuration));
    }

    [Fact]
    public async Task UpsertSuccessAsync_NegativeStaleDuration_Throws()
    {
        CustomRouteDnsCacheRepository repository = CreateRepository();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => repository.UpsertSuccessAsync(
                Guid.NewGuid(),
                "example.com",
                ["8.8.8.8"],
                CacheDuration,
                TimeSpan.FromMinutes(-5)));
    }

    [Fact]
    public async Task UpsertSuccessAsync_StaleShorterThanCache_Throws()
    {
        CustomRouteDnsCacheRepository repository = CreateRepository();

        await Assert.ThrowsAsync<ArgumentException>(
            () => repository.UpsertSuccessAsync(
                Guid.NewGuid(),
                "example.com",
                ["8.8.8.8"],
                TimeSpan.FromHours(1),
                TimeSpan.FromMinutes(30)));
    }

    [Fact]
    public async Task UpsertFailureAsync_EmptyError_Throws()
    {
        CustomRouteDnsCacheRepository repository = CreateRepository();

        await Assert.ThrowsAsync<ArgumentException>(
            () => repository.UpsertFailureAsync(
                Guid.NewGuid(),
                "example.com",
                "   "));
    }

    [Fact]
    public async Task RemoveAsync_EmptyEntryId_Throws()
    {
        CustomRouteDnsCacheRepository repository = CreateRepository();

        await Assert.ThrowsAsync<ArgumentException>(
            () => repository.RemoveAsync(Guid.Empty));
    }

    private static string CreatePath()
    {
        return Path.Combine(
            Path.GetTempPath(),
            "IranDirect.Tests",
            Guid.NewGuid().ToString("N"),
            "custom-route-dns-cache.json");
    }

    private static CustomRouteDnsCacheStore CreateStore() =>
        new(CreatePath());

    private static CustomRouteDnsCacheRepository CreateRepository(
        CustomRouteDnsCacheStore? store = null,
        TimeProvider? timeProvider = null)
    {
        store ??= CreateStore();
        timeProvider ??= new FakeTimeProvider(Start);

        return new CustomRouteDnsCacheRepository(store, timeProvider);
    }

    private sealed class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset _now;

        public FakeTimeProvider(DateTimeOffset now)
        {
            _now = now;
        }

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan delta) => _now += delta;
    }
}
