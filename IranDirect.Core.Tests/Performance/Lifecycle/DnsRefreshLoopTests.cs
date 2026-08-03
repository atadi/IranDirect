using System.Net;
using IranDirect.Core.CustomRoutes;
using IranDirect.Testing.Performance.Lifecycle;

namespace IranDirect.Core.Tests.Performance.Lifecycle;

/// <summary>
/// DNS refresh loop: drives the production
/// <see cref="CustomRouteResolver"/> and
/// <see cref="CustomRouteDnsCacheRepository"/> through fresh hits,
/// expiry-driven refreshes, stale fallback, and failure-then-recovery
/// using only the simulated clock and the scripted resolver. Asserts
/// the cache keeps exactly one record per domain and that the stored
/// address set never grows across equivalent cycles.
/// </summary>
public sealed class DnsRefreshLoopTests
{
    private const string Domain = "cache-host.example";

    private static readonly CustomRouteDnsCacheOptions s_options = new();

    [Fact]
    public async Task FreshHits_RepeatedResolves_DoNotReResolveWithinCacheWindow()
    {
        const int resolves = 100;

        using SimulatedRuntimeEnvironment environment = new();

        await SeedDomainAsync(environment, "203.0.113.10");

        // First resolve populates the cache.
        await environment.CustomRouteResolver.ResolveAsync();

        int afterFirst =
            environment.DnsResolver.GetResolutionCount(Domain);

        Assert.Equal(1, afterFirst);

        // Every subsequent resolve inside the cache window must be a
        // cache hit, so the scripted resolver is never called again.
        ServiceSimulationRunResult result =
            await ServiceSimulationRunner.RunAsync(
                environment,
                new ServiceSimulationPlan(
                    "dns-fresh",
                    [new ResolveCustomRoutesStep(resolves)]));

        Assert.Equal(resolves, result.Metrics.CustomRouteResolutions);
        Assert.Equal(
            afterFirst,
            environment.DnsResolver.GetResolutionCount(Domain));

        await ServiceSimulationVerifier
            .AssertDnsCacheRecordCountAsync(environment, Domain, 1);
    }

    [Fact]
    public async Task ExpiredEntries_RefreshExactlyOncePerExpiry()
    {
        const int rounds = 10;

        using SimulatedRuntimeEnvironment environment = new();

        await SeedDomainAsync(environment, "203.0.113.20");

        await environment.CustomRouteResolver.ResolveAsync();
        Assert.Equal(
            1,
            environment.DnsResolver.GetResolutionCount(Domain));

        // Each round advances past the cache duration, so exactly one
        // refresh per round must reach the resolver.
        for (int i = 0; i < rounds; i++)
        {
            environment.Time.Advance(
                s_options.DnsCacheDuration + TimeSpan.FromMinutes(1));

            await environment.CustomRouteResolver.ResolveAsync();
        }

        Assert.Equal(
            1 + rounds,
            environment.DnsResolver.GetResolutionCount(Domain));

        // Refreshes must replace the record, never accumulate.
        await ServiceSimulationVerifier
            .AssertDnsCacheRecordCountAsync(environment, Domain, 1);

        IReadOnlyList<CustomRouteDnsCacheEntry> entries =
            await LoadEntriesAsync(environment);

        Assert.Single(entries);
        Assert.Single(entries[0].IPv4Addresses);
    }

    [Fact]
    public async Task FailureThenRecovery_FallsBackStaleThenRepairsCache()
    {
        using SimulatedRuntimeEnvironment environment = new();

        await SeedDomainAsync(environment, "203.0.113.30");

        await environment.CustomRouteResolver.ResolveAsync();

        CustomRouteDnsCacheEntry initial =
            (await LoadEntriesAsync(environment))[0];

        Assert.Equal(
            ["203.0.113.30"],
            initial.IPv4Addresses);

        // Expire the record and make the resolver fail: the cached
        // (now stale) addresses must survive as the fallback.
        environment.Time.Advance(
            s_options.DnsCacheDuration + TimeSpan.FromMinutes(1));
        environment.DnsResolver.SetFailing(Domain, failing: true);

        await environment.CustomRouteResolver.ResolveAsync();

        IReadOnlyList<CustomRouteDnsCacheEntry> afterFailure =
            await LoadEntriesAsync(environment);

        Assert.Single(afterFailure);
        Assert.Equal(
            ["203.0.113.30"],
            afterFailure[0].IPv4Addresses);

        // Recovery: the resolver succeeds again with a new address and
        // the cache converges on exactly that address.
        environment.DnsResolver.SetFailing(Domain, failing: false);
        environment.DnsResolver.SetAddresses(
            Domain,
            IPAddress.Parse("203.0.113.31"));
        environment.Time.Advance(
            s_options.DnsCacheDuration + TimeSpan.FromMinutes(1));

        await environment.CustomRouteResolver.ResolveAsync();

        IReadOnlyList<CustomRouteDnsCacheEntry> recovered =
            await LoadEntriesAsync(environment);

        Assert.Single(recovered);
        Assert.Equal(
            ["203.0.113.31"],
            recovered[0].IPv4Addresses);

        await ServiceSimulationVerifier
            .AssertDnsCacheRecordCountAsync(environment, Domain, 1);
        ServiceSimulationVerifier.AssertNoPendingTempFiles(environment);
    }

    [Fact]
    public async Task LongTransitionSequence_KeepsSingleRecordAndNoAddressGrowth()
    {
        const int rounds = 40;

        using SimulatedRuntimeEnvironment environment = new();

        await SeedDomainAsync(environment, "203.0.113.40");

        // Deterministic fresh / expired / failing rotation.
        for (int i = 0; i < rounds; i++)
        {
            bool failing = i % 4 == 3;

            environment.DnsResolver.SetFailing(Domain, failing);

            if (!failing)
            {
                environment.DnsResolver.SetAddresses(
                    Domain,
                    IPAddress.Parse($"203.0.113.{40 + (i % 5)}"));
            }

            environment.Time.Advance(
                s_options.DnsCacheDuration + TimeSpan.FromMinutes(1));

            await environment.CustomRouteResolver.ResolveAsync();

            IReadOnlyList<CustomRouteDnsCacheEntry> entries =
                await LoadEntriesAsync(environment);

            // Invariant on every single round: one record, one address.
            Assert.Single(entries);
            Assert.Single(entries[0].IPv4Addresses);
        }

        await ServiceSimulationVerifier
            .AssertDnsCacheRecordCountAsync(environment, Domain, 1);
        ServiceSimulationVerifier.AssertNoPendingTempFiles(environment);
    }

    private static async Task SeedDomainAsync(
        SimulatedRuntimeEnvironment environment,
        string address)
    {
        await environment.CustomRouteService.AddAsync(
            CustomRouteEntryType.Domain,
            Domain);

        environment.DnsResolver.SetAddresses(
            Domain,
            IPAddress.Parse(address));
    }

    private static async Task<IReadOnlyList<CustomRouteDnsCacheEntry>>
        LoadEntriesAsync(SimulatedRuntimeEnvironment environment)
    {
        CustomRouteDnsCacheStore store =
            new(environment.DnsCachePath);

        CustomRouteDnsCacheCollection collection =
            await store.LoadAsync();

        return collection.Entries;
    }
}
