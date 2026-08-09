using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using PathVeer.Core.CustomRoutes;
using PathVeer.Core.Observability.Telemetry;
using PathVeer.Core.Testing.FaultInjection;
using Xunit;

namespace PathVeer.Core.Tests.Observability.Telemetry;

[Collection("RuntimeCycleTelemetry")]
public sealed class CustomRouteRefreshTelemetryTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan StaleDuration = TimeSpan.FromHours(2);

    // ---- harness ---------------------------------------------------------

    private sealed class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset _now;
        public FakeTimeProvider(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan delta) => _now += delta;
    }

    private static Func<string, CancellationToken, Task<
        IReadOnlyList<IPAddress>>> Lookup(params IPAddress[] addresses) =>
        (_, _) => Task.FromResult<IReadOnlyList<IPAddress>>(addresses);

    private static CustomRouteEntry CreateEntry(
        string value,
        bool enabled = true) =>
        new()
        {
            Id = Guid.NewGuid(),
            Type = CustomRouteEntryType.Domain,
            Value = value,
            Enabled = enabled,
            CreatedAt = Now,
            ModifiedAt = Now
        };

    private static CustomRouteResolver CreateResolver(
        out CustomRouteDnsCacheStore store,
        out FakeTimeProvider clock,
        Func<string, CancellationToken, Task<
            IReadOnlyList<IPAddress>>> lookup,
        params CustomRouteEntry[] entries)
    {
        string dir = Path.Combine(
            Path.GetTempPath(),
            "IranDirect.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        store = new CustomRouteDnsCacheStore(
            Path.Combine(dir, "dns-cache.json"));
        clock = new FakeTimeProvider(Now);

        CustomRouteDnsCacheRepository cacheRepository = new(store, clock);

        string routesDir = Path.Combine(
            Path.GetTempPath(),
            "IranDirect.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(routesDir);
        CustomRouteRepository repository = new(
            new CustomRouteStore(
                Path.Combine(routesDir, "custom-routes.json")));

        if (entries.Length > 0)
        {
            repository.MutateAsync(collection =>
                collection with { Entries = entries })
                .GetAwaiter().GetResult();
        }

        return new CustomRouteResolver(
            repository,
            cacheRepository,
            new CustomRouteDnsCacheOptions(),
            clock,
            lookup);
    }

    private static CustomRouteResolver CreateResolverOnStore(
        CustomRouteDnsCacheStore store,
        FakeTimeProvider clock,
        Func<string, CancellationToken, Task<
            IReadOnlyList<IPAddress>>> lookup,
        params CustomRouteEntry[] entries)
    {
        CustomRouteDnsCacheRepository cacheRepository = new(store, clock);

        string routesDir = Path.Combine(
            Path.GetTempPath(),
            "IranDirect.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(routesDir);
        CustomRouteRepository repository = new(
            new CustomRouteStore(
                Path.Combine(routesDir, "custom-routes.json")));

        if (entries.Length > 0)
        {
            repository.MutateAsync(collection =>
                collection with { Entries = entries })
                .GetAwaiter().GetResult();
        }

        return new CustomRouteResolver(
            repository,
            cacheRepository,
            new CustomRouteDnsCacheOptions(),
            clock,
            lookup);
    }

    private static ActivityListener CreateActivityListener(
        ConcurrentQueue<Activity> started,
        ConcurrentQueue<Activity> stopped)
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = s =>
                s.Name == PathVeerTelemetry.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = a => started.Enqueue(a),
            ActivityStopped = a => stopped.Enqueue(a),
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    private static MeterListener CreateMeterListener(
        ConcurrentQueue<long> lookups,
        ConcurrentQueue<double> durations)
    {
        var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == PathVeerTelemetry.SourceName)
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>(
            (instrument, value, tags, state) =>
            {
                if (instrument.Name == PathVeerMetricNames.DnsLookups)
                {
                    lookups.Enqueue(value);
                }
            });
        listener.SetMeasurementEventCallback<double>(
            (instrument, value, tags, state) =>
            {
                if (instrument.Name ==
                    PathVeerMetricNames.DnsLookupDuration)
                {
                    durations.Enqueue(value);
                }
            });
        listener.Start();
        return listener;
    }

    // ---- tests -----------------------------------------------------------

    [Fact]
    public async Task FreshCache_OneCacheRead_NoLookup_NoResolveChild()
    {
        CustomRouteEntry entry = CreateEntry("example.com");

        // Seed the cache with one successful lookup BEFORE any listener is
        // attached, so only the second (fresh-hit) resolve is observed.
        CustomRouteResolver seeder = CreateResolver(
            out CustomRouteDnsCacheStore seedStore,
            out FakeTimeProvider seedClock,
            Lookup(IPAddress.Parse("10.0.0.1")),
            entry);
        CustomRouteResolutionResult seeded =
            await seeder.ResolveAsync();
        Assert.True(seeded.AllSucceeded);

        var started = new ConcurrentQueue<Activity>();
        var stopped = new ConcurrentQueue<Activity>();
        var lookups = new ConcurrentQueue<long>();
        var durations = new ConcurrentQueue<double>();

        using var al = CreateActivityListener(started, stopped);
        using var ml = CreateMeterListener(lookups, durations);

        // Re-resolve using the SAME store + SAME entry id (fresh cache hit).
        CustomRouteResolver resolver = CreateResolverOnStore(
            seedStore,
            seedClock,
            Lookup(IPAddress.Parse("10.0.0.9")),
            entry);

        await resolver.ResolveAsync();

        Activity root = Assert.Single(
            started.Where(a =>
                a.OperationName ==
                PathVeerActivityNames.CustomRouteRefresh));
        Assert.Equal(
            PathVeerTagValues.OperationCustomRouteRefresh,
            root.Tags.Single(t =>
                t.Key == PathVeerTagNames.Operation).Value);

        Assert.Contains(
            stopped,
            a => a.OperationName ==
                PathVeerActivityNames.DnsCacheRead);

        // Fresh cache: no DNS lookup child and no lookup counter.
        Assert.DoesNotContain(
            stopped,
            a => a.OperationName ==
                PathVeerActivityNames.DnsResolve);
        Assert.Empty(lookups);
        Assert.Empty(durations);
    }

    [Fact]
    public async Task StaleCache_SuccessfulRefresh_CacheReadResolveCacheWrite()
    {
        CustomRouteEntry entry = CreateEntry("example.com");

        // Seed a fresh entry BEFORE the listener is attached.
        CustomRouteResolver seeder = CreateResolver(
            out CustomRouteDnsCacheStore seedStore,
            out FakeTimeProvider seedClock,
            Lookup(IPAddress.Parse("10.0.0.1")),
            entry);
        await seeder.ResolveAsync();

        // Expire into the stale window.
        seedClock.Advance(CacheDuration + TimeSpan.FromMinutes(1));

        var started = new ConcurrentQueue<Activity>();
        var stopped = new ConcurrentQueue<Activity>();
        var lookups = new ConcurrentQueue<long>();
        var durations = new ConcurrentQueue<double>();

        using var al = CreateActivityListener(started, stopped);
        using var ml = CreateMeterListener(lookups, durations);

        // Second resolver on the SAME store + entry id, now stale.
        CustomRouteResolver resolver = CreateResolverOnStore(
            seedStore,
            seedClock,
            Lookup(IPAddress.Parse("10.0.0.2")),
            entry);

        CustomRouteResolutionResult result =
            await resolver.ResolveAsync();

        Assert.True(result.AllSucceeded);

        Assert.Contains(
            stopped,
            a => a.OperationName ==
                PathVeerActivityNames.DnsCacheRead);
        Activity resolve = Assert.Single(
            stopped.Where(a =>
                a.OperationName ==
                PathVeerActivityNames.DnsResolve));
        Assert.Contains(
            stopped,
            a => a.OperationName ==
                PathVeerActivityNames.DnsCacheWrite);

        Assert.Equal(1, lookups.Count);
        Assert.Equal(1, durations.Count);

        Assert.Equal(
            PathVeerTagValues.OperationDnsResolve,
            resolve.Tags.Single(t =>
                t.Key == PathVeerTagNames.Operation).Value);
        Assert.Equal(
            PathVeerTagValues.SourceCustom,
            resolve.Tags.Single(t =>
                t.Key == PathVeerTagNames.Source).Value);
    }

    [Fact]
    public async Task MissAndSuccessfulDns_EmitsLookupAndCacheWrite()
    {
        var started = new ConcurrentQueue<Activity>();
        var stopped = new ConcurrentQueue<Activity>();
        var lookups = new ConcurrentQueue<long>();
        var durations = new ConcurrentQueue<double>();

        using var al = CreateActivityListener(started, stopped);
        using var ml = CreateMeterListener(lookups, durations);

        CustomRouteResolver resolver = CreateResolver(
            out _,
            out _,
            Lookup(IPAddress.Parse("10.0.0.1")),
            CreateEntry("example.com"));

        CustomRouteResolutionResult result =
            await resolver.ResolveAsync();

        Assert.True(result.AllSucceeded);

        Assert.Contains(
            stopped,
            a => a.OperationName ==
                PathVeerActivityNames.DnsCacheRead);
        Assert.Contains(
            stopped,
            a => a.OperationName ==
                PathVeerActivityNames.DnsResolve);
        Assert.Contains(
            stopped,
            a => a.OperationName ==
                PathVeerActivityNames.DnsCacheWrite);

        Assert.Equal(1, lookups.Count);
        Assert.Equal(1, durations.Count);

        Activity root = Assert.Single(
            started.Where(a =>
                a.OperationName ==
                PathVeerActivityNames.CustomRouteRefresh));
        Assert.Equal(
            PathVeerTagValues.Success,
            root.Tags.Single(t =>
                t.Key == PathVeerTagNames.Outcome).Value);
    }

    [Fact]
    public async Task MissAndDnsFailure_RootFailureLookupCounted()
    {
        var started = new ConcurrentQueue<Activity>();
        var stopped = new ConcurrentQueue<Activity>();
        var lookups = new ConcurrentQueue<long>();
        var durations = new ConcurrentQueue<double>();

        using var al = CreateActivityListener(started, stopped);
        using var ml = CreateMeterListener(lookups, durations);

        CustomRouteResolver resolver = CreateResolver(
            out _,
            out _,
            (_, _) => throw new TimeoutException("DNS timed out"),
            CreateEntry("example.com"));

        CustomRouteResolutionResult result =
            await resolver.ResolveAsync();

        Assert.False(result.AllSucceeded);

        // The lookup was still attempted -> counter increments once.
        Assert.Equal(1, lookups.Count);
        Assert.Equal(1, durations.Count);

        Activity resolve = Assert.Single(
            stopped.Where(a =>
                a.OperationName ==
                PathVeerActivityNames.DnsResolve));
        Assert.Equal(
            PathVeerTagValues.Failure,
            resolve.Tags.Single(t =>
                t.Key == PathVeerTagNames.Outcome).Value);
        Assert.Equal(
            PathVeerTagValues.FailureTimeout,
            resolve.Tags.Single(t =>
                t.Key == PathVeerTagNames.FailureCategory).Value);

        Activity root = Assert.Single(
            started.Where(a =>
                a.OperationName ==
                PathVeerActivityNames.CustomRouteRefresh));
        Assert.Equal(
            PathVeerTagValues.Failure,
            root.Tags.Single(t =>
                t.Key == PathVeerTagNames.Outcome).Value);
        Assert.Equal(
            PathVeerTagValues.FailureDns,
            root.Tags.Single(t =>
                t.Key == PathVeerTagNames.FailureCategory).Value);
    }

    [Fact]
    public async Task StaleFallback_LookupFails_RootOkServesStaleData()
    {
        CustomRouteEntry entry = CreateEntry("example.com");

        // Seed a fresh entry BEFORE the listener is attached.
        CustomRouteResolver seeder = CreateResolver(
            out CustomRouteDnsCacheStore seedStore,
            out FakeTimeProvider seedClock,
            Lookup(IPAddress.Parse("10.0.0.1")),
            entry);
        await seeder.ResolveAsync();

        // Expire into the stale window but still inside StaleUntil, so stale
        // data remains usable.
        seedClock.Advance(CacheDuration + TimeSpan.FromMinutes(1));

        var started = new ConcurrentQueue<Activity>();
        var stopped = new ConcurrentQueue<Activity>();
        var lookups = new ConcurrentQueue<long>();
        var durations = new ConcurrentQueue<double>();

        using var al = CreateActivityListener(started, stopped);
        using var ml = CreateMeterListener(lookups, durations);

        // Second resolver on the SAME store + entry id; the lookup FAILS.
        CustomRouteResolver resolver = CreateResolverOnStore(
            seedStore,
            seedClock,
            (_, _) => throw new TimeoutException("DNS timed out"),
            entry);

        CustomRouteResolutionResult result =
            await resolver.ResolveAsync();

        // Usable stale data is returned -> overall success, not failure.
        Assert.True(result.AllSucceeded);
        Assert.Single(result.Prefixes); // stale 10.0.0.1/32 served

        Activity resolve = Assert.Single(
            stopped.Where(a =>
                a.OperationName ==
                PathVeerActivityNames.DnsResolve));
        Assert.Equal(
            PathVeerTagValues.Failure,
            resolve.Tags.Single(t =>
                t.Key == PathVeerTagNames.Outcome).Value);
        Assert.Equal(
            PathVeerTagValues.FailureTimeout,
            resolve.Tags.Single(t =>
                t.Key == PathVeerTagNames.FailureCategory).Value);

        // The root is Ok because stale data was served.
        Activity root = Assert.Single(
            started.Where(a =>
                a.OperationName ==
                PathVeerActivityNames.CustomRouteRefresh));
        Assert.Equal(
            PathVeerTagValues.Success,
            root.Tags.Single(t =>
                t.Key == PathVeerTagNames.Outcome).Value);

        // cache_state=stale on the cache read and the failure cache write.
        Assert.All(
            stopped.Where(a =>
                a.OperationName == PathVeerActivityNames.DnsCacheRead
                || a.OperationName == PathVeerActivityNames.DnsCacheWrite),
            a => Assert.Equal(
                PathVeerTagValues.CacheStale,
                a.Tags.Single(t =>
                    t.Key == PathVeerTagNames.CacheState).Value));

        // One lookup attempt -> one counter increment, one duration sample,
        // and no second (duplicate) failed-workflow metric.
        Assert.Equal(1, lookups.Count);
        Assert.Equal(1, durations.Count);
    }

    [Fact]
    public async Task MultipleDomains_OneLookupPerDomain_NoPerAddress()
    {
        var started = new ConcurrentQueue<Activity>();
        var stopped = new ConcurrentQueue<Activity>();
        var lookups = new ConcurrentQueue<long>();
        var durations = new ConcurrentQueue<double>();

        using var al = CreateActivityListener(started, stopped);
        using var ml = CreateMeterListener(lookups, durations);

        CustomRouteResolver resolver = CreateResolver(
            out _,
            out _,
            Lookup(IPAddress.Parse("10.0.0.1"), IPAddress.Parse("10.0.0.2")),
            CreateEntry("a.example.com"),
            CreateEntry("b.example.com"));

        await resolver.ResolveAsync();

        // Two real lookups -> two increments, two durations.
        Assert.Equal(2, lookups.Count);
        Assert.Equal(2, durations.Count);

        // No per-address spans: every activity is a known DNS activity name.
        foreach (Activity a in stopped)
        {
            Assert.Contains(
                a.OperationName,
                new[]
                {
                    PathVeerActivityNames.DnsCacheRead,
                    PathVeerActivityNames.DnsResolve,
                    PathVeerActivityNames.DnsCacheWrite,
                    PathVeerActivityNames.CustomRouteRefresh
                });
        }
    }

    [Fact]
    public async Task NoListener_BehaviorUnchanged()
    {
        CustomRouteResolver resolver = CreateResolver(
            out _,
            out _,
            Lookup(IPAddress.Parse("10.0.0.1")),
            CreateEntry("example.com"));

        CustomRouteResolutionResult result =
            await resolver.ResolveAsync();

        Assert.True(result.AllSucceeded);
        Assert.Single(result.Prefixes);
    }

    [Fact]
    public async Task NoSensitiveTagsOnAnyDnsActivity()
    {
        var started = new ConcurrentQueue<Activity>();
        var stopped = new ConcurrentQueue<Activity>();

        using var al = CreateActivityListener(started, stopped);

        CustomRouteResolver resolver = CreateResolver(
            out _,
            out _,
            Lookup(IPAddress.Parse("10.0.0.1")),
            CreateEntry("example.com"));

        await resolver.ResolveAsync();

        foreach (Activity a in stopped.Concat(started))
        {
            foreach (KeyValuePair<string, object?> tag in a.TagObjects)
            {
                Assert.DoesNotContain(
                    "domain",
                    tag.Key,
                    StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(
                    "address",
                    tag.Key,
                    StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(
                    "ip",
                    tag.Key,
                    StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(
                    "prefix",
                    tag.Key,
                    StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(
                    "url",
                    tag.Key,
                    StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(
                    "ttl",
                    tag.Key,
                    StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
