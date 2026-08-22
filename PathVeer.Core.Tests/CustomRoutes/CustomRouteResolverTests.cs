using System.IO;
using System.Net;
using PathVeer.Core.CustomRoutes;
using PathVeer.Core.Persistence;

namespace PathVeer.Core.Tests.CustomRoutes;

public sealed class CustomRouteResolverTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan SeedCacheDuration =
        TimeSpan.FromMinutes(30);

    private static readonly TimeSpan SeedStaleDuration =
        TimeSpan.FromHours(2);

    [Fact]
    public async Task ResolveAsync_WhenRepositoryEmpty_ReturnsEmpty()
    {
        ResolverHarness harness = CreateHarness();

        CustomRouteResolutionResult result =
            await harness.Resolver.ResolveAsync();

        Assert.Empty(result.Prefixes);
        Assert.True(result.AllSucceeded);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public async Task ResolveAsync_WhenOnlyDisabledEntries_ReturnsEmpty()
    {
        ResolverHarness harness = CreateHarness(
            CreateEntry(
                CustomRouteEntryType.IpAddress,
                "8.8.8.8",
                enabled: false));

        CustomRouteResolutionResult result =
            await harness.Resolver.ResolveAsync();

        Assert.Empty(result.Prefixes);
        Assert.Empty(result.Failures);
    }

    [Fact]
    public async Task ResolveAsync_IpAddress_BecomesHostRoute()
    {
        ResolverHarness harness = CreateHarness(
            CreateEntry(
                CustomRouteEntryType.IpAddress,
                "8.8.8.8"));

        CustomRouteResolutionResult result =
            await harness.Resolver.ResolveAsync();

        string prefix = Assert.Single(result.Prefixes);
        Assert.Equal("8.8.8.8/32", prefix);
        Assert.True(result.AllSucceeded);
    }

    [Fact]
    public async Task ResolveAsync_Cidr_PassesThrough()
    {
        ResolverHarness harness = CreateHarness(
            CreateEntry(
                CustomRouteEntryType.Cidr,
                "10.0.0.0/24"));

        CustomRouteResolutionResult result =
            await harness.Resolver.ResolveAsync();

        string prefix = Assert.Single(result.Prefixes);
        Assert.Equal("10.0.0.0/24", prefix);
    }

    [Fact]
    public async Task ResolveAsync_Domain_SingleARecord_BecomesHostRoute()
    {
        ResolverHarness harness = CreateHarness(
            Lookup(IPAddress.Parse("93.184.216.34")),
            CreateEntry(
                CustomRouteEntryType.Domain,
                "example.com"));

        CustomRouteResolutionResult result =
            await harness.Resolver.ResolveAsync();

        string prefix = Assert.Single(result.Prefixes);
        Assert.Equal("93.184.216.34/32", prefix);
        Assert.True(result.AllSucceeded);
    }

    [Fact]
    public async Task ResolveAsync_Domain_MultipleARecords_ProduceOneRouteEach()
    {
        ResolverHarness harness = CreateHarness(
            Lookup(
                IPAddress.Parse("93.184.216.34"),
                IPAddress.Parse("93.184.215.14")),
            CreateEntry(
                CustomRouteEntryType.Domain,
                "example.com"));

        CustomRouteResolutionResult result =
            await harness.Resolver.ResolveAsync();

        Assert.Equal(
            new[] { "93.184.215.14/32", "93.184.216.34/32" },
            result.Prefixes);
    }

    [Fact]
    public async Task ResolveAsync_Domain_DuplicateARecords_AreDeduplicated()
    {
        ResolverHarness harness = CreateHarness(
            Lookup(
                IPAddress.Parse("93.184.216.34"),
                IPAddress.Parse("93.184.216.34")),
            CreateEntry(
                CustomRouteEntryType.Domain,
                "example.com"));

        CustomRouteResolutionResult result =
            await harness.Resolver.ResolveAsync();

        string prefix = Assert.Single(result.Prefixes);
        Assert.Equal("93.184.216.34/32", prefix);
    }

    [Fact]
    public async Task ResolveAsync_Domain_Ipv6Records_AreIgnored()
    {
        ResolverHarness harness = CreateHarness(
            Lookup(
                IPAddress.Parse("::1"),
                IPAddress.Parse("1.2.3.4")),
            CreateEntry(
                CustomRouteEntryType.Domain,
                "example.com"));

        CustomRouteResolutionResult result =
            await harness.Resolver.ResolveAsync();

        string prefix = Assert.Single(result.Prefixes);
        Assert.Equal("1.2.3.4/32", prefix);
    }

    [Fact]
    public async Task ResolveAsync_Domain_OnlyIpv6Records_RecordsFailure()
    {
        ResolverHarness harness = CreateHarness(
            Lookup(IPAddress.Parse("::1")),
            CreateEntry(
                CustomRouteEntryType.Domain,
                "example.com"));

        CustomRouteResolutionResult result =
            await harness.Resolver.ResolveAsync();

        Assert.Empty(result.Prefixes);
        Assert.False(result.AllSucceeded);

        CustomRouteResolutionFailure failure =
            Assert.Single(result.Failures);
        Assert.Equal(
            CustomRouteEntryType.Domain,
            failure.Type);
        Assert.Equal("example.com", failure.Value);
        Assert.Contains(
            "No IPv4 A records",
            failure.Reason);
    }

    [Fact]
    public async Task ResolveAsync_Domain_DnsThrows_RecordsFailureAndContinues()
    {
        ResolverHarness harness = CreateHarness(
            (host, _) =>
                host == "broken.example"
                    ? throw new InvalidOperationException("NXDOMAIN")
                    : Task.FromResult<IReadOnlyList<IPAddress>>(
                        [IPAddress.Parse("5.6.7.8")]),
            CreateEntry(
                CustomRouteEntryType.Domain,
                "broken.example"),
            CreateEntry(
                CustomRouteEntryType.Domain,
                "healthy.example"));

        CustomRouteResolutionResult result =
            await harness.Resolver.ResolveAsync();

        string prefix = Assert.Single(result.Prefixes);
        Assert.Equal("5.6.7.8/32", prefix);

        CustomRouteResolutionFailure failure =
            Assert.Single(result.Failures);
        Assert.Equal("broken.example", failure.Value);
        Assert.Contains(
            "DNS resolution failed",
            failure.Reason);
    }

    [Fact]
    public async Task ResolveAsync_MixedEntries_DeduplicatesAcrossTypes()
    {
        ResolverHarness harness = CreateHarness(
            (host, _) =>
                Task.FromResult<IReadOnlyList<IPAddress>>(
                    [IPAddress.Parse("8.8.8.8")]),
            CreateEntry(
                CustomRouteEntryType.IpAddress,
                "8.8.8.8"),
            CreateEntry(
                CustomRouteEntryType.Cidr,
                "10.0.0.0/24"),
            CreateEntry(
                CustomRouteEntryType.Domain,
                "duplicate.example"));

        CustomRouteResolutionResult result =
            await harness.Resolver.ResolveAsync();

        Assert.Equal(
            new[] { "8.8.8.8/32", "10.0.0.0/24" },
            result.Prefixes);
        Assert.True(result.AllSucceeded);
    }

    [Fact]
    public async Task ResolveAsync_ResultIsOrderedByAddressThenPrefixLength()
    {
        ResolverHarness harness = CreateHarness(
            CreateEntry(
                CustomRouteEntryType.Cidr,
                "10.0.0.0/24"),
            CreateEntry(
                CustomRouteEntryType.IpAddress,
                "192.168.0.1"),
            CreateEntry(
                CustomRouteEntryType.IpAddress,
                "1.2.3.4"));

        CustomRouteResolutionResult result =
            await harness.Resolver.ResolveAsync();

        Assert.Equal(
            new[]
            {
                "1.2.3.4/32",
                "10.0.0.0/24",
                "192.168.0.1/32"
            },
            result.Prefixes);
    }

    [Fact]
    public async Task ResolveAsync_CorruptEntryValue_RecordsFailureWithoutThrowing()
    {
        ResolverHarness harness = CreateHarness(
            CreateEntry(
                CustomRouteEntryType.IpAddress,
                "not-an-ip"));

        CustomRouteResolutionResult result =
            await harness.Resolver.ResolveAsync();

        Assert.Empty(result.Prefixes);

        CustomRouteResolutionFailure failure =
            Assert.Single(result.Failures);
        Assert.Equal("not-an-ip", failure.Value);
        Assert.Contains("Invalid IPv4", failure.Reason);
    }

    [Fact]
    public async Task ResolveAsync_Cancellation_IsPropagated()
    {
        ResolverHarness harness = CreateHarness(
            (_, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult<IReadOnlyList<IPAddress>>(
                    [IPAddress.Parse("1.2.3.4")]);
            },
            CreateEntry(
                CustomRouteEntryType.Domain,
                "example.com"));

        using CancellationTokenSource cts = new();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => harness.Resolver.ResolveAsync(cts.Token));
    }

    [Fact]
    public async Task ResolveAsync_FreshCache_ReturnsCachedAddressesWithoutDns()
    {
        Guid domainId = Guid.NewGuid();
        CustomRouteEntry entry = CreateEntry(
            CustomRouteEntryType.Domain,
            "example.com",
            id: domainId);

        bool dnsCalled = false;

        ResolverHarness harness = CreateHarness(
            (_, _) =>
            {
                dnsCalled = true;
                return Task.FromResult<IReadOnlyList<IPAddress>>(
                    [IPAddress.Parse("1.1.1.1")]);
            },
            entry);

        await harness.CacheRepository.UpsertSuccessAsync(
            domainId,
            "example.com",
            ["93.184.216.34"],
            SeedCacheDuration,
            SeedStaleDuration);

        CustomRouteResolutionResult result =
            await harness.Resolver.ResolveAsync();

        Assert.False(dnsCalled);

        string prefix = Assert.Single(result.Prefixes);
        Assert.Equal("93.184.216.34/32", prefix);
        Assert.True(result.AllSucceeded);

        CustomRouteResolutionDiagnostic diagnostic =
            Assert.Single(result.Diagnostics);
        Assert.Equal(
            CustomRouteResolutionStatus.FreshCacheHit,
            diagnostic.Status);
        Assert.Equal("example.com", diagnostic.Value);
    }

    [Fact]
    public async Task ResolveAsync_ExpiredCache_RefreshesFromDns()
    {
        Guid domainId = Guid.NewGuid();
        CustomRouteEntry entry = CreateEntry(
            CustomRouteEntryType.Domain,
            "example.com",
            id: domainId);

        ResolverHarness harness = CreateHarness(
            Lookup(IPAddress.Parse("1.2.3.4")),
            entry);

        await harness.CacheRepository.UpsertSuccessAsync(
            domainId,
            "example.com",
            ["93.184.216.34"],
            SeedCacheDuration,
            SeedStaleDuration);

        harness.Clock.Advance(TimeSpan.FromHours(1));

        CustomRouteResolutionResult result =
            await harness.Resolver.ResolveAsync();

        string prefix = Assert.Single(result.Prefixes);
        Assert.Equal("1.2.3.4/32", prefix);

        CustomRouteResolutionDiagnostic diagnostic =
            Assert.Single(result.Diagnostics);
        Assert.Equal(
            CustomRouteResolutionStatus.Refreshed,
            diagnostic.Status);

        CustomRouteDnsCacheEntry cache =
            (await harness.CacheRepository.GetByEntryIdAsync(domainId))!;
        Assert.NotNull(cache);
        Assert.Equal(["1.2.3.4"], cache.IPv4Addresses);
        Assert.Equal(
            Now + TimeSpan.FromHours(1),
            cache.LastSucceededAt);
    }

    [Fact]
    public async Task ResolveAsync_MissingCache_ResolvesAndPersists()
    {
        Guid domainId = Guid.NewGuid();
        CustomRouteEntry entry = CreateEntry(
            CustomRouteEntryType.Domain,
            "example.com",
            id: domainId);

        ResolverHarness harness = CreateHarness(
            Lookup(IPAddress.Parse("93.184.216.34")),
            entry);

        CustomRouteResolutionResult result =
            await harness.Resolver.ResolveAsync();

        Assert.Equal(["93.184.216.34/32"], result.Prefixes);

        CustomRouteDnsCacheEntry cache =
            (await harness.CacheRepository.GetByEntryIdAsync(domainId))!;
        Assert.NotNull(cache);
        Assert.Equal(["93.184.216.34"], cache.IPv4Addresses);
        Assert.Equal(
            Now + harness.Options.DnsCacheDuration,
            cache.ExpiresAt);
        Assert.Equal(
            Now + harness.Options.DnsMaxStaleDuration,
            cache.StaleUntil);
        Assert.Equal(Now, cache.LastSucceededAt);
    }

    [Fact]
    public async Task ResolveAsync_SuccessfulRefresh_ReplacesOldAddresses()
    {
        Guid domainId = Guid.NewGuid();
        CustomRouteEntry entry = CreateEntry(
            CustomRouteEntryType.Domain,
            "example.com",
            id: domainId);

        ResolverHarness harness = CreateHarness(
            Lookup(
                IPAddress.Parse("2.2.2.2"),
                IPAddress.Parse("2.2.2.2")),
            entry);

        await harness.CacheRepository.UpsertSuccessAsync(
            domainId,
            "example.com",
            ["1.1.1.1"],
            SeedCacheDuration,
            SeedStaleDuration);

        harness.Clock.Advance(TimeSpan.FromHours(1));

        CustomRouteResolutionResult result =
            await harness.Resolver.ResolveAsync();

        string prefix = Assert.Single(result.Prefixes);
        Assert.Equal("2.2.2.2/32", prefix);

        CustomRouteDnsCacheEntry cache =
            (await harness.CacheRepository.GetByEntryIdAsync(domainId))!;
        Assert.NotNull(cache);
        Assert.Equal(["2.2.2.2"], cache.IPv4Addresses);
    }

    [Fact]
    public async Task ResolveAsync_DnsFailure_UsesStaleKnownGoodAddresses()
    {
        Guid domainId = Guid.NewGuid();
        CustomRouteEntry entry = CreateEntry(
            CustomRouteEntryType.Domain,
            "example.com",
            id: domainId);

        ResolverHarness harness = CreateHarness(
            (_, _) => throw new InvalidOperationException("NXDOMAIN"),
            entry);

        await harness.CacheRepository.UpsertSuccessAsync(
            domainId,
            "example.com",
            ["93.184.216.34"],
            SeedCacheDuration,
            SeedStaleDuration);

        harness.Clock.Advance(TimeSpan.FromHours(1));

        CustomRouteResolutionResult result =
            await harness.Resolver.ResolveAsync();

        string prefix = Assert.Single(result.Prefixes);
        Assert.Equal("93.184.216.34/32", prefix);
        Assert.True(result.AllSucceeded);
        Assert.Empty(result.Failures);

        CustomRouteResolutionDiagnostic diagnostic =
            Assert.Single(result.Diagnostics);
        Assert.Equal(
            CustomRouteResolutionStatus.StaleFallback,
            diagnostic.Status);
        Assert.Contains("NXDOMAIN", diagnostic.Reason);
        Assert.Contains(
            "stale",
            diagnostic.Reason,
            StringComparison.OrdinalIgnoreCase);

        CustomRouteDnsCacheEntry cache =
            (await harness.CacheRepository.GetByEntryIdAsync(domainId))!;
        Assert.NotNull(cache);
        Assert.Equal(["93.184.216.34"], cache.IPv4Addresses);
        Assert.Equal(
            "DNS resolution failed: NXDOMAIN",
            cache.LastError);
    }

    [Fact]
    public async Task ResolveAsync_DnsTimeout_UsesStaleFallback()
    {
        Guid domainId = Guid.NewGuid();
        CustomRouteEntry entry = CreateEntry(
            CustomRouteEntryType.Domain,
            "example.com",
            id: domainId);

        CustomRouteDnsCacheOptions options = new()
        {
            DnsTimeout = TimeSpan.FromMilliseconds(100)
        };

        ResolverHarness harness = CreateHarness(
            options,
            async (_, token) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(10), token);
                return (IReadOnlyList<IPAddress>)[];
            },
            entry);

        await harness.CacheRepository.UpsertSuccessAsync(
            domainId,
            "example.com",
            ["93.184.216.34"],
            SeedCacheDuration,
            SeedStaleDuration);

        harness.Clock.Advance(TimeSpan.FromHours(1));

        CustomRouteResolutionResult result =
            await harness.Resolver.ResolveAsync();

        string prefix = Assert.Single(result.Prefixes);
        Assert.Equal("93.184.216.34/32", prefix);

        CustomRouteResolutionDiagnostic diagnostic =
            Assert.Single(result.Diagnostics);
        Assert.Equal(
            CustomRouteResolutionStatus.StaleFallback,
            diagnostic.Status);
        Assert.Contains("timed out", diagnostic.Reason);
    }

    [Fact]
    public async Task ResolveAsync_StaleExpiredFailure_ReturnsNoPrefixes()
    {
        Guid domainId = Guid.NewGuid();
        CustomRouteEntry entry = CreateEntry(
            CustomRouteEntryType.Domain,
            "example.com",
            id: domainId);

        ResolverHarness harness = CreateHarness(
            (_, _) => throw new InvalidOperationException("NXDOMAIN"),
            entry);

        await harness.CacheRepository.UpsertSuccessAsync(
            domainId,
            "example.com",
            ["93.184.216.34"],
            SeedCacheDuration,
            SeedStaleDuration);

        harness.Clock.Advance(TimeSpan.FromHours(3));

        CustomRouteResolutionResult result =
            await harness.Resolver.ResolveAsync();

        Assert.Empty(result.Prefixes);
        Assert.False(result.AllSucceeded);

        CustomRouteResolutionFailure failure =
            Assert.Single(result.Failures);
        Assert.Equal("example.com", failure.Value);
        Assert.Contains("DNS resolution failed", failure.Reason);

        CustomRouteResolutionDiagnostic diagnostic =
            Assert.Single(result.Diagnostics);
        Assert.Equal(
            CustomRouteResolutionStatus.Failure,
            diagnostic.Status);
    }

    [Fact]
    public async Task ResolveAsync_NoPriorCacheFailure_ReturnsNoPrefixes()
    {
        ResolverHarness harness = CreateHarness(
            (_, _) => throw new InvalidOperationException("NXDOMAIN"),
            CreateEntry(
                CustomRouteEntryType.Domain,
                "example.com"));

        CustomRouteResolutionResult result =
            await harness.Resolver.ResolveAsync();

        Assert.Empty(result.Prefixes);
        Assert.Single(result.Failures);
        Assert.Single(result.Diagnostics);
    }

    [Fact]
    public async Task ResolveAsync_NoARecords_FollowsFailurePath()
    {
        ResolverHarness harness = CreateHarness(
            Lookup(IPAddress.Parse("::1")),
            CreateEntry(
                CustomRouteEntryType.Domain,
                "example.com"));

        CustomRouteResolutionResult result =
            await harness.Resolver.ResolveAsync();

        Assert.Empty(result.Prefixes);

        CustomRouteResolutionFailure failure =
            Assert.Single(result.Failures);
        Assert.Contains("No IPv4 A records", failure.Reason);

        CustomRouteResolutionDiagnostic diagnostic =
            Assert.Single(result.Diagnostics);
        Assert.Equal(
            CustomRouteResolutionStatus.Failure,
            diagnostic.Status);
    }

    [Fact]
    public async Task ResolveAsync_NoARecords_WithStaleCache_UsesStaleFallback()
    {
        Guid domainId = Guid.NewGuid();
        CustomRouteEntry entry = CreateEntry(
            CustomRouteEntryType.Domain,
            "example.com",
            id: domainId);

        ResolverHarness harness = CreateHarness(
            Lookup(IPAddress.Parse("::1")),
            entry);

        await harness.CacheRepository.UpsertSuccessAsync(
            domainId,
            "example.com",
            ["93.184.216.34"],
            SeedCacheDuration,
            SeedStaleDuration);

        harness.Clock.Advance(TimeSpan.FromHours(1));

        CustomRouteResolutionResult result =
            await harness.Resolver.ResolveAsync();

        Assert.Equal(["93.184.216.34/32"], result.Prefixes);

        CustomRouteResolutionDiagnostic diagnostic =
            Assert.Single(result.Diagnostics);
        Assert.Equal(
            CustomRouteResolutionStatus.StaleFallback,
            diagnostic.Status);
        Assert.Contains("No IPv4 A records", diagnostic.Reason);
    }

    [Fact]
    public async Task ResolveAsync_CacheCleanup_RemovesDeletedAndNonDomainEntries()
    {
        Guid domainId = Guid.NewGuid();
        Guid deletedId = Guid.NewGuid();
        Guid ipId = Guid.NewGuid();

        ResolverHarness harness = CreateHarness(
            Lookup(IPAddress.Parse("1.1.1.1")),
            CreateEntry(
                CustomRouteEntryType.Domain,
                "example.com",
                id: domainId),
            CreateEntry(
                CustomRouteEntryType.IpAddress,
                "8.8.8.8",
                id: ipId));

        await harness.CacheRepository.UpsertSuccessAsync(
            domainId,
            "example.com",
            ["9.9.9.9"],
            SeedCacheDuration,
            SeedStaleDuration);

        await harness.CacheRepository.UpsertSuccessAsync(
            deletedId,
            "deleted.example.com",
            ["9.9.9.8"],
            SeedCacheDuration,
            SeedStaleDuration);

        await harness.CacheRepository.UpsertSuccessAsync(
            ipId,
            "cached-ip.example.com",
            ["9.9.9.7"],
            SeedCacheDuration,
            SeedStaleDuration);

        CustomRouteResolutionResult result =
            await harness.Resolver.ResolveAsync();

        Assert.Equal(["8.8.8.8/32", "9.9.9.9/32"], result.Prefixes);

        IReadOnlyList<CustomRouteDnsCacheEntry> remaining =
            await harness.CacheRepository.GetAllAsync();

        CustomRouteDnsCacheEntry only =
            Assert.Single(remaining);
        Assert.Equal(domainId, only.CustomRouteEntryId);
    }

    [Fact]
    public async Task ResolveAsync_DisabledDomain_CacheIsRetained()
    {
        Guid disabledId = Guid.NewGuid();
        bool dnsCalled = false;

        ResolverHarness harness = CreateHarness(
            (_, _) =>
            {
                dnsCalled = true;
                return Task.FromResult<IReadOnlyList<IPAddress>>(
                    [IPAddress.Parse("1.1.1.1")]);
            },
            CreateEntry(
                CustomRouteEntryType.Domain,
                "disabled.example.com",
                enabled: false,
                id: disabledId));

        await harness.CacheRepository.UpsertSuccessAsync(
            disabledId,
            "disabled.example.com",
            ["9.9.9.9"],
            SeedCacheDuration,
            SeedStaleDuration);

        CustomRouteResolutionResult result =
            await harness.Resolver.ResolveAsync();

        Assert.False(dnsCalled);
        Assert.Empty(result.Prefixes);
        Assert.Empty(result.Diagnostics);

        Assert.NotNull(
            await harness.CacheRepository.GetByEntryIdAsync(disabledId));
    }

    [Fact]
    public async Task ResolveAsync_BoundedConcurrency_DoesNotExceedLimit()
    {
        CustomRouteEntry[] entries = Enumerable.Range(0, 10)
            .Select(i => CreateEntry(
                CustomRouteEntryType.Domain,
                $"host-{i}.example.com"))
            .ToArray();

        int active = 0;
        int maxActive = 0;
        object sync = new();

        ResolverHarness harness = CreateHarness(
            async (_, token) =>
            {
                lock (sync)
                {
                    active++;
                    maxActive = Math.Max(maxActive, active);
                }

                try
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(100), token);
                    return (IReadOnlyList<IPAddress>)
                        [IPAddress.Parse("1.1.1.1")];
                }
                finally
                {
                    lock (sync)
                    {
                        active--;
                    }
                }
            },
            entries);

        CustomRouteResolutionResult result =
            await harness.Resolver.ResolveAsync();

        Assert.InRange(maxActive, 1, 4);
        Assert.Equal(
            10,
            result.Diagnostics.Count(
                d => d.Status == CustomRouteResolutionStatus.Refreshed));
    }

    [Fact]
    public async Task ResolveAsync_DomainCompletionOrder_ProducesDeterministicOutput()
    {
        CustomRouteEntry[] entries = Enumerable.Range(0, 6)
            .Select(i => CreateEntry(
                CustomRouteEntryType.Domain,
                $"host-{i}.example.com"))
            .ToArray();

        ResolverHarness harness = CreateHarness(
            async (host, token) =>
            {
                int i = int.Parse(
                    host.Split('.')[0].Split('-')[1]);
                await Task.Delay(
                    i % 2 == 0
                        ? TimeSpan.FromMilliseconds(30)
                        : TimeSpan.FromMilliseconds(5),
                    token);
                return (IReadOnlyList<IPAddress>)
                    [IPAddress.Parse($"1.1.1.{i + 1}")];
            },
            entries);

        CustomRouteResolutionResult first =
            await harness.Resolver.ResolveAsync();

        CustomRouteResolutionResult second =
            await harness.Resolver.ResolveAsync();

        Assert.Equal(first.Prefixes, second.Prefixes);
        Assert.Equal(
            new[]
            {
                "1.1.1.1/32",
                "1.1.1.2/32",
                "1.1.1.3/32",
                "1.1.1.4/32",
                "1.1.1.5/32",
                "1.1.1.6/32"
            },
            first.Prefixes);
    }

    [Fact]
    public async Task ResolveAsync_DuplicateCachedDnsAndStaticPrefixes_Collapse()
    {
        Guid cachedId = Guid.NewGuid();
        Guid dnsId = Guid.NewGuid();

        ResolverHarness harness = CreateHarness(
            Lookup(IPAddress.Parse("8.8.8.8")),
            CreateEntry(
                CustomRouteEntryType.Domain,
                "cached.example.com",
                id: cachedId),
            CreateEntry(
                CustomRouteEntryType.Domain,
                "dns.example.com",
                id: dnsId),
            CreateEntry(
                CustomRouteEntryType.IpAddress,
                "8.8.8.8"));

        await harness.CacheRepository.UpsertSuccessAsync(
            cachedId,
            "cached.example.com",
            ["8.8.8.8"],
            SeedCacheDuration,
            SeedStaleDuration);

        CustomRouteResolutionResult result =
            await harness.Resolver.ResolveAsync();

        string prefix = Assert.Single(result.Prefixes);
        Assert.Equal("8.8.8.8/32", prefix);
        Assert.True(result.AllSucceeded);
    }

    [Fact]
    public async Task ResolveAsync_CorruptCache_PersistsDiagnosticsWithoutCrashing()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "IranDirect.Tests",
            Guid.NewGuid().ToString("N"),
            "custom-route-dns-cache.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "{ not valid json");
        // Production wiring uses BackupRollback recovery mode (which quarantines
        // a corrupt primary to a collision-safe evidence file); the ordinary
        // (string) ctor is FailClosed. Pass the options overload to force the
        // BackupRollback path used by the Service composition root.
        CustomRouteDnsCacheStore corruptStore =
            new(path, null);

        ResolverHarness harness = CreateHarness(
            corruptStore,
            Lookup(IPAddress.Parse("1.2.3.4")),
            CreateEntry(
                CustomRouteEntryType.Domain,
                "example.com"));

        CustomRouteResolutionResult result =
            await harness.Resolver.ResolveAsync();

        Assert.Equal(["1.2.3.4/32"], result.Prefixes);

        // A corrupt cache with NO valid backup now fails clearly (it does not
        // silently return stale/empty). The resolver surfaces that as a read
        // failure diagnostic and continues, refreshing the cache. In FailClosed
        // terms the corrupt primary is never silently overwritten; with the
        // production BackupRollback wiring a .bak would be quarantined to a
        // collision-safe evidence file instead.
        Assert.Contains(
            result.Diagnostics,
            d => d.Status == CustomRouteResolutionStatus.Failure
                && (d.Reason ?? "").Contains("DNS cache read failed"));
    }

    [Fact]
    public void Validate_InvalidDurations_Throw()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CustomRouteDnsCacheOptions.Validate(
                new CustomRouteDnsCacheOptions
                {
                    DnsCacheDuration = TimeSpan.Zero
                }));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CustomRouteDnsCacheOptions.Validate(
                new CustomRouteDnsCacheOptions
                {
                    DnsTimeout = TimeSpan.FromMinutes(-1)
                }));

        Assert.Throws<ArgumentException>(() =>
            CustomRouteDnsCacheOptions.Validate(
                new CustomRouteDnsCacheOptions
                {
                    DnsCacheDuration = TimeSpan.FromHours(2),
                    DnsMaxStaleDuration = TimeSpan.FromMinutes(30)
                }));
    }

    [Fact]
    public void Constructor_InvalidOptions_Throws()
    {
        CustomRouteDnsCacheOptions invalid = new()
        {
            DnsCacheDuration = TimeSpan.Zero
        };

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CustomRouteResolver(
                CreateRepository(),
                new CustomRouteDnsCacheRepository(
                    CreateCacheStore(),
                    new FakeTimeProvider(Now)),
                invalid,
                new FakeTimeProvider(Now)));
    }

    private static Func<
        string,
        CancellationToken,
        Task<IReadOnlyList<IPAddress>>> Lookup(
            params IPAddress[] addresses) =>
        (_, _) =>
            Task.FromResult<IReadOnlyList<IPAddress>>(
                addresses);

    private static ResolverHarness CreateHarness(
        params CustomRouteEntry[] entries) =>
        CreateHarnessCore(null, null, null, entries);

    private static ResolverHarness CreateHarness(
        Func<string, CancellationToken, Task<IReadOnlyList<IPAddress>>> lookup,
        params CustomRouteEntry[] entries) =>
        CreateHarnessCore(null, null, lookup, entries);

    private static ResolverHarness CreateHarness(
        CustomRouteDnsCacheOptions options,
        Func<string, CancellationToken, Task<IReadOnlyList<IPAddress>>> lookup,
        params CustomRouteEntry[] entries) =>
        CreateHarnessCore(options, null, lookup, entries);

    private static ResolverHarness CreateHarness(
        CustomRouteDnsCacheStore cacheStore,
        Func<string, CancellationToken, Task<IReadOnlyList<IPAddress>>> lookup,
        params CustomRouteEntry[] entries) =>
        CreateHarnessCore(null, cacheStore, lookup, entries);

    private static ResolverHarness CreateHarnessCore(
        CustomRouteDnsCacheOptions? options,
        CustomRouteDnsCacheStore? cacheStore,
        Func<string, CancellationToken, Task<IReadOnlyList<IPAddress>>>? lookup,
        params CustomRouteEntry[] entries)
    {
        CustomRouteRepository repository = CreateRepository();

        if (entries.Length > 0)
        {
            repository.MutateAsync(collection =>
                collection with
                {
                    Entries = entries
                }).GetAwaiter().GetResult();
        }

        FakeTimeProvider clock = new(Now);
        CustomRouteDnsCacheOptions resolvedOptions =
            options ?? new CustomRouteDnsCacheOptions();
        CustomRouteDnsCacheStore store =
            cacheStore ?? CreateCacheStore();
        CustomRouteDnsCacheRepository cacheRepository =
            new(store, clock);

        CustomRouteResolver resolver = new(
            repository,
            cacheRepository,
            resolvedOptions,
            clock,
            lookup ?? Lookup());

        return new ResolverHarness(
            resolver,
            cacheRepository,
            store,
            clock,
            resolvedOptions);
    }

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

    private static CustomRouteDnsCacheStore CreateCacheStore()
    {
        return new CustomRouteDnsCacheStore(
            Path.Combine(
                Path.GetTempPath(),
                "IranDirect.Tests",
                Guid.NewGuid().ToString("N"),
                "custom-route-dns-cache.json"));
    }

    private static CustomRouteEntry CreateEntry(
        CustomRouteEntryType type,
        string value,
        bool enabled = true,
        Guid? id = null) =>
        new()
        {
            Id = id ?? Guid.NewGuid(),
            Type = type,
            Value = value,
            Enabled = enabled,
            CreatedAt = Now,
            ModifiedAt = Now
        };

    private sealed record ResolverHarness(
        CustomRouteResolver Resolver,
        CustomRouteDnsCacheRepository CacheRepository,
        CustomRouteDnsCacheStore CacheStore,
        FakeTimeProvider Clock,
        CustomRouteDnsCacheOptions Options);

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

    // Collision-safe evidence files use "<base>.<timestamp>.<id>"; this helper
    // asserts that at least one such file exists for the given base prefix.
    private static bool ExistsWithPrefix(string basePath)
    {
        string? dir = Path.GetDirectoryName(basePath);
        string fileName = Path.GetFileName(basePath);
        return dir is not null
            && Directory.Exists(dir)
            && Directory.EnumerateFiles(dir, fileName + ".*").Any();
    }
}
