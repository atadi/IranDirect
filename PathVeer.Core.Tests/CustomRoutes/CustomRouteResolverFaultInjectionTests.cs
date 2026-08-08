using System.Net;
using PathVeer.Core.CustomRoutes;
using PathVeer.Core.Testing.FaultInjection;

namespace PathVeer.Core.Tests.CustomRoutes;

public sealed class CustomRouteResolverFaultInjectionTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan SeedCacheDuration =
        TimeSpan.FromMinutes(30);

    private static readonly TimeSpan SeedStaleDuration =
        TimeSpan.FromHours(2);

    private const string DnsFaultReason =
        "DNS resolution failed: Fault injected at DnsLookup.";

    [Fact]
    public async Task ResolveAsync_FreshCache_FaultPolicyActive_DnsIsNotCalled()
    {
        Guid domainId = Guid.NewGuid();
        CustomRouteEntry entry = CreateEntry(
            CustomRouteEntryType.Domain,
            "example.com",
            id: domainId);

        int dnsCalls = 0;

        ResolverHarness harness = CreateHarness(
            FaultInjectionPolicy.For(
                [FaultInjectionPoint.DnsLookup]),
            (_, _) =>
            {
                Interlocked.Increment(ref dnsCalls);
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

        Assert.Equal(0, dnsCalls);

        string prefix = Assert.Single(result.Prefixes);
        Assert.Equal("93.184.216.34/32", prefix);
        Assert.True(result.AllSucceeded);

        CustomRouteResolutionDiagnostic diagnostic =
            Assert.Single(result.Diagnostics);
        Assert.Equal(
            CustomRouteResolutionStatus.FreshCacheHit,
            diagnostic.Status);
    }

    [Fact]
    public async Task ResolveAsync_StaleCache_DnsLookupFault_ReturnsStaleFallbackAndPreservesKnownGoodAddresses()
    {
        Guid domainId = Guid.NewGuid();
        CustomRouteEntry entry = CreateEntry(
            CustomRouteEntryType.Domain,
            "example.com",
            id: domainId);

        ResolverHarness harness = CreateHarness(
            FaultInjectionPolicy.For(
                [FaultInjectionPoint.DnsLookup]),
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
        Assert.Equal("93.184.216.34/32", prefix);
        Assert.True(result.AllSucceeded);
        Assert.Empty(result.Failures);

        CustomRouteResolutionDiagnostic diagnostic =
            Assert.Single(result.Diagnostics);
        Assert.Equal(
            CustomRouteResolutionStatus.StaleFallback,
            diagnostic.Status);
        Assert.Contains(
            "Fault injected at DnsLookup",
            diagnostic.Reason);
        Assert.Contains(
            "stale",
            diagnostic.Reason,
            StringComparison.OrdinalIgnoreCase);

        CustomRouteDnsCacheEntry cache =
            (await harness.CacheRepository
                .GetByEntryIdAsync(domainId))!;
        Assert.NotNull(cache);
        Assert.Equal(["93.184.216.34"], cache.IPv4Addresses);
        Assert.Equal(Now, cache.LastSucceededAt);
        Assert.Equal(DnsFaultReason, cache.LastError);
    }

    [Fact]
    public async Task ResolveAsync_MissingCache_DnsLookupFault_RecordsConciseFailureWithoutDnsCall()
    {
        int dnsCalls = 0;

        ResolverHarness harness = CreateHarness(
            FaultInjectionPolicy.For(
                [FaultInjectionPoint.DnsLookup]),
            (_, _) =>
            {
                Interlocked.Increment(ref dnsCalls);
                return Task.FromResult<IReadOnlyList<IPAddress>>(
                    [IPAddress.Parse("1.2.3.4")]);
            },
            CreateEntry(
                CustomRouteEntryType.Domain,
                "example.com"));

        CustomRouteResolutionResult result =
            await harness.Resolver.ResolveAsync();

        Assert.Equal(0, dnsCalls);
        Assert.Empty(result.Prefixes);
        Assert.False(result.AllSucceeded);

        CustomRouteResolutionFailure failure =
            Assert.Single(result.Failures);
        Assert.Equal("example.com", failure.Value);
        Assert.Equal(DnsFaultReason, failure.Reason);

        CustomRouteResolutionDiagnostic diagnostic =
            Assert.Single(result.Diagnostics);
        Assert.Equal(
            CustomRouteResolutionStatus.Failure,
            diagnostic.Status);
        Assert.Equal(DnsFaultReason, diagnostic.Reason);
    }

    [Fact]
    public async Task ResolveAsync_StaleExpired_DnsLookupFault_RecordsFailureWithoutPrefixes()
    {
        Guid domainId = Guid.NewGuid();
        CustomRouteEntry entry = CreateEntry(
            CustomRouteEntryType.Domain,
            "example.com",
            id: domainId);

        ResolverHarness harness = CreateHarness(
            FaultInjectionPolicy.For(
                [FaultInjectionPoint.DnsLookup]),
            Lookup(IPAddress.Parse("1.2.3.4")),
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
        Assert.Equal(DnsFaultReason, failure.Reason);

        CustomRouteResolutionDiagnostic diagnostic =
            Assert.Single(result.Diagnostics);
        Assert.Equal(
            CustomRouteResolutionStatus.Failure,
            diagnostic.Status);
    }

    [Fact]
    public async Task ResolveAsync_DnsLookupFault_AfterScopeDisposal_Recovers()
    {
        Guid domainId = Guid.NewGuid();
        CustomRouteEntry entry = CreateEntry(
            CustomRouteEntryType.Domain,
            "example.com",
            id: domainId);

        int dnsCalls = 0;

        ResolverHarness harness = CreateHarness(
            (_, _) =>
            {
                Interlocked.Increment(ref dnsCalls);
                return Task.FromResult<IReadOnlyList<IPAddress>>(
                    [IPAddress.Parse("1.2.3.4")]);
            },
            entry);

        CustomRouteResolutionResult first;
        using (FaultInjectionScope scope =
            FaultInjectionScope.Fail(
                FaultInjectionPoint.DnsLookup))
        {
            first = await harness.Resolver.ResolveAsync();
        }

        Assert.Empty(first.Prefixes);
        Assert.Single(first.Failures);
        Assert.Single(first.Diagnostics);
        Assert.Equal(0, dnsCalls);

        CustomRouteResolutionResult second =
            await harness.Resolver.ResolveAsync();

        Assert.Equal(["1.2.3.4/32"], second.Prefixes);
        Assert.True(second.AllSucceeded);
        Assert.Equal(1, dnsCalls);
    }

    [Fact]
    public async Task ResolveAsync_InjectedPolicy_WithoutAmbientScope_TriggersDnsFault()
    {
        ResolverHarness harness = CreateHarness(
            FaultInjectionPolicy.For(
                [FaultInjectionPoint.DnsLookup]),
            Lookup(IPAddress.Parse("1.2.3.4")),
            CreateEntry(
                CustomRouteEntryType.Domain,
                "example.com"));

        Assert.False(FaultInjectionScope.IsActive);

        CustomRouteResolutionResult result =
            await harness.Resolver.ResolveAsync();

        Assert.Empty(result.Prefixes);
        Assert.Single(result.Failures);
        Assert.Equal(DnsFaultReason, result.Failures[0].Reason);
    }

    [Fact]
    public async Task ResolveAsync_AmbientScope_OverridesInjectedPolicy()
    {
        ResolverHarness injectedNeverHarness = CreateHarness(
            FaultInjectionPolicy.Never,
            Lookup(IPAddress.Parse("1.2.3.4")),
            CreateEntry(
                CustomRouteEntryType.Domain,
                "never.example"));

        CustomRouteResolutionResult fromNever;
        using (FaultInjectionScope scope =
            FaultInjectionScope.Fail(
                FaultInjectionPoint.DnsLookup))
        {
            fromNever =
                await injectedNeverHarness.Resolver.ResolveAsync();
        }

        Assert.Empty(fromNever.Prefixes);
        Assert.Single(fromNever.Failures);
        Assert.Equal(
            DnsFaultReason,
            fromNever.Failures[0].Reason);

        ResolverHarness injectedDnsHarness = CreateHarness(
            FaultInjectionPolicy.For(
                [FaultInjectionPoint.DnsLookup]),
            Lookup(IPAddress.Parse("2.3.4.5")),
            CreateEntry(
                CustomRouteEntryType.Domain,
                "unrelated.example"));

        CustomRouteResolutionResult fromDns;
        using (FaultInjectionScope scope =
            FaultInjectionScope.Fail(
                FaultInjectionPoint.HttpRequest))
        {
            fromDns =
                await injectedDnsHarness.Resolver.ResolveAsync();
        }

        Assert.Equal(["2.3.4.5/32"], fromDns.Prefixes);
        Assert.True(fromDns.AllSucceeded);
    }

    [Fact]
    public async Task ResolveAsync_UnrelatedPoint_DoesNotTriggerDnsFault()
    {
        ResolverHarness harness = CreateHarness(
            FaultInjectionPolicy.For(
                [FaultInjectionPoint.HttpRequest]),
            Lookup(IPAddress.Parse("1.2.3.4")),
            CreateEntry(
                CustomRouteEntryType.Domain,
                "example.com"));

        CustomRouteResolutionResult result =
            await harness.Resolver.ResolveAsync();

        Assert.Equal(["1.2.3.4/32"], result.Prefixes);
        Assert.True(result.AllSucceeded);
        Assert.Empty(result.Failures);

        CustomRouteResolutionDiagnostic diagnostic =
            Assert.Single(result.Diagnostics);
        Assert.Equal(
            CustomRouteResolutionStatus.Refreshed,
            diagnostic.Status);
    }

    [Fact]
    public async Task ResolveAsync_FaultedDomain_DoesNotBlockFreshCachedDomain()
    {
        Guid healthyId = Guid.NewGuid();

        ResolverHarness harness = CreateHarness(
            FaultInjectionPolicy.For(
                [FaultInjectionPoint.DnsLookup]),
            Lookup(IPAddress.Parse("1.1.1.1")),
            CreateEntry(
                CustomRouteEntryType.Domain,
                "faulted.example.com"),
            CreateEntry(
                CustomRouteEntryType.Domain,
                "cached.example.com",
                id: healthyId));

        await harness.CacheRepository.UpsertSuccessAsync(
            healthyId,
            "cached.example.com",
            ["9.9.9.9"],
            SeedCacheDuration,
            SeedStaleDuration);

        CustomRouteResolutionResult result =
            await harness.Resolver.ResolveAsync();

        string prefix = Assert.Single(result.Prefixes);
        Assert.Equal("9.9.9.9/32", prefix);
        Assert.False(result.AllSucceeded);

        CustomRouteResolutionFailure failure =
            Assert.Single(result.Failures);
        Assert.Equal("faulted.example.com", failure.Value);
        Assert.Equal(DnsFaultReason, failure.Reason);

        Assert.Equal(2, result.Diagnostics.Count);
        Assert.Contains(
            result.Diagnostics,
            d => d.Status ==
                CustomRouteResolutionStatus.FreshCacheHit);
        Assert.Contains(
            result.Diagnostics,
            d => d.Status ==
                CustomRouteResolutionStatus.Failure);
    }

    [Fact]
    public async Task ResolveAsync_ConcurrentResolvers_IndependentPolicies_DoNotInterfere()
    {
        ResolverHarness faultHarness = CreateHarness(
            FaultInjectionPolicy.For(
                [FaultInjectionPoint.DnsLookup]),
            Lookup(IPAddress.Parse("1.1.1.1")),
            CreateEntry(
                CustomRouteEntryType.Domain,
                "faulted.example.com"));

        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        ResolverHarness healthyHarness = CreateHarness(
            FaultInjectionPolicy.Never,
            async (_, _) =>
            {
                await release.Task;
                return (IReadOnlyList<IPAddress>)
                    [IPAddress.Parse("2.2.2.2")];
            },
            CreateEntry(
                CustomRouteEntryType.Domain,
                "healthy.example.com"));

        Task<CustomRouteResolutionResult> faultTask =
            faultHarness.Resolver.ResolveAsync();
        Task<CustomRouteResolutionResult> healthyTask =
            healthyHarness.Resolver.ResolveAsync();

        CustomRouteResolutionResult faultResult =
            await faultTask;

        Assert.Empty(faultResult.Prefixes);
        Assert.Single(faultResult.Failures);
        Assert.Equal(
            DnsFaultReason,
            faultResult.Failures[0].Reason);

        release.SetResult();

        CustomRouteResolutionResult healthyResult =
            await healthyTask;

        Assert.Equal(["2.2.2.2/32"], healthyResult.Prefixes);
        Assert.True(healthyResult.AllSucceeded);
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
        CreateHarnessCore(null, null, entries);

    private static ResolverHarness CreateHarness(
        Func<string, CancellationToken, Task<IReadOnlyList<IPAddress>>> lookup,
        params CustomRouteEntry[] entries) =>
        CreateHarnessCore(null, lookup, entries);

    private static ResolverHarness CreateHarness(
        IFaultInjectionPolicy? faultPolicy,
        Func<string, CancellationToken, Task<IReadOnlyList<IPAddress>>> lookup,
        params CustomRouteEntry[] entries) =>
        CreateHarnessCore(faultPolicy, lookup, entries);

    private static ResolverHarness CreateHarness(
        IFaultInjectionPolicy? faultPolicy,
        params CustomRouteEntry[] entries) =>
        CreateHarnessCore(faultPolicy, null, entries);

    private static ResolverHarness CreateHarnessCore(
        IFaultInjectionPolicy? faultPolicy,
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
        CustomRouteDnsCacheOptions options = new();
        CustomRouteDnsCacheStore store = CreateCacheStore();
        CustomRouteDnsCacheRepository cacheRepository =
            new(store, clock);

        CustomRouteResolver resolver = new(
            repository,
            cacheRepository,
            options,
            clock,
            lookup ?? Lookup(),
            faultPolicy);

        return new ResolverHarness(
            resolver,
            cacheRepository,
            clock);
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
        FakeTimeProvider Clock);

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
