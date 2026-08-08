using PathVeer.Core.Configuration;
using PathVeer.Core.CustomRoutes;
using PathVeer.Core.Networking;
using PathVeer.Core.Prefixes;
using PathVeer.Core.Runtime;
using PathVeer.Core.Vpn;
using PathVeer.Core.Tests.TestDoubles;

namespace PathVeer.Core.Tests.Runtime;

public sealed class CustomRouteMergeTests
{
    private readonly RuntimePlanner _planner =
        new(new DesiredConfigurationValidator());

    [Fact]
    public async Task ObservePrefixesAsync_WithoutResolver_ReturnsFilePrefixes()
    {
        ObservationSourceFixture fixture = CreateFixture(
            ["203.0.113.0/24"]);

        IReadOnlyList<string> prefixes =
            await fixture.Source.ObservePrefixesAsync();

        string prefix = Assert.Single(prefixes);
        Assert.Equal("203.0.113.0/24", prefix);
    }

    [Fact]
    public async Task ObservePrefixesAsync_WithResolver_MergesAndDeduplicates()
    {
        ObservationSourceFixture fixture = CreateFixture(
            ["203.0.113.0/24", "1.2.3.4/32"],
            resolver: CreateResolver(
                new[]
                {
                    CreateEntry(
                        CustomRouteEntryType.IpAddress,
                        "8.8.8.8"),
                    CreateEntry(
                        CustomRouteEntryType.Domain,
                        "example.com")
                },
                (host, _) =>
                    Task.FromResult<IReadOnlyList<System.Net.IPAddress>>(
                        host == "example.com"
                            ? [System.Net.IPAddress.Parse("1.2.3.4")]
                            : [])));

        IReadOnlyList<string> prefixes =
            await fixture.Source.ObservePrefixesAsync();

        Assert.Equal(
            new[]
            {
                "203.0.113.0/24",
                "1.2.3.4/32",
                "8.8.8.8/32"
            },
            prefixes);
    }

    [Fact]
    public async Task ObservePrefixesAsync_WhenResolverReturnsEmpty_ReturnsFilePrefixes()
    {
        ObservationSourceFixture fixture = CreateFixture(
            ["203.0.113.0/24"],
            resolver: CreateResolver());

        IReadOnlyList<string> prefixes =
            await fixture.Source.ObservePrefixesAsync();

        string prefix = Assert.Single(prefixes);
        Assert.Equal("203.0.113.0/24", prefix);
    }

    [Fact]
    public async Task ObservePrefixesAsync_WhenFileEmpty_CustomPrefixesFlowThrough()
    {
        ObservationSourceFixture fixture = CreateFixture(
            [],
            resolver: CreateResolver(
                CreateEntry(
                    CustomRouteEntryType.Cidr,
                    "10.0.0.0/24")));

        IReadOnlyList<string> prefixes =
            await fixture.Source.ObservePrefixesAsync();

        string prefix = Assert.Single(prefixes);
        Assert.Equal("10.0.0.0/24", prefix);
    }

    [Fact]
    public async Task Planner_ReceivesMergedDistinctPrefixSet()
    {
        ObservationSourceFixture fixture = CreateFixture(
            ["203.0.113.0/24"],
            resolver: CreateResolver(
                CreateEntry(
                    CustomRouteEntryType.IpAddress,
                    "8.8.8.8"),
                CreateEntry(
                    CustomRouteEntryType.Cidr,
                    "10.0.0.0/24")));

        IReadOnlyList<string> merged =
            await fixture.Source.ObservePrefixesAsync();

        DesiredRuntime plan =
            _planner.Plan(
                ConfigurationDefaults.Create() with
                {
                    Enabled = true
                },
                CreateHealthyObservedRuntime() with
                {
                    Prefixes = merged
                });

        Assert.True(plan.CanReconcile);
        Assert.Equal(
            new[]
            {
                "203.0.113.0/24",
                "8.8.8.8/32",
                "10.0.0.0/24"
            },
            plan.PrefixRoutes
                .Select(route =>
                    route.DestinationPrefix)
                .ToArray());
    }

    private static ObservationSourceFixture CreateFixture(
        IReadOnlyList<string> prefixFileLines,
        CustomRouteResolver? resolver = null)
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "IranDirect.Tests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(directory);

        string prefixFile =
            Path.Combine(directory, "prefixes", "IR", "ipv4-prefixes.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(prefixFile)!);
        File.WriteAllLines(
            prefixFile,
            prefixFileLines);

        string profilePath =
            Path.Combine(directory, "vpn-profile.ovpn");

        CountryPrefixStore prefixStore = new(directory);

        OpenVpnEndpointProvider endpointProvider =
            new(
                profilePath,
                new OpenVpnProfileParser(),
                new VpnEndpointResolver());

        PathVeerRuntimeObservationSource source =
            new(
                profilePath,
                endpointProvider,
                new GatewayDetector(),
                prefixStore,
                () => DirectCountryCode.IR,
                new FakeRouteManager(),
                customRouteResolver: resolver);

        return new ObservationSourceFixture(source);
    }

    private static CustomRouteResolver CreateResolver(
        params CustomRouteEntry[] entries) =>
        CreateResolver(
            entries,
            (_, _) =>
                Task.FromResult<IReadOnlyList<System.Net.IPAddress>>([]));

    private static CustomRouteResolver CreateResolver(
        CustomRouteEntry[] entries,
        Func<string, CancellationToken, Task<IReadOnlyList<System.Net.IPAddress>>> lookup)
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

        CustomRouteDnsCacheStore cacheStore = new(
            Path.Combine(
                Path.GetTempPath(),
                "IranDirect.Tests",
                Guid.NewGuid().ToString("N"),
                "custom-route-dns-cache.json"));

        return new CustomRouteResolver(
            repository,
            new CustomRouteDnsCacheRepository(
                cacheStore,
                TimeProvider.System),
            new CustomRouteDnsCacheOptions(),
            TimeProvider.System,
            lookup);
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

    private static CustomRouteEntry CreateEntry(
        CustomRouteEntryType type,
        string value) =>
        new()
        {
            Id = Guid.NewGuid(),
            Type = type,
            Value = value,
            Enabled = true,
            CreatedAt = DateTimeOffset.UtcNow,
            ModifiedAt = DateTimeOffset.UtcNow
        };

    private static ObservedRuntime CreateHealthyObservedRuntime()
    {
        return new ObservedRuntime
        {
            VpnProfileExists = true,
            VpnProfileValid = true,
            DirectGateway =
                new ObservedDirectGateway
                {
                    Address = "192.168.100.1",
                    InterfaceIndex = 30,
                    InterfaceName = "Ethernet",
                    InterfaceMetric = 10
                },
            VpnEndpoints =
            [
                new ObservedVpnEndpoint
                {
                    Host = "5.160.74.148",
                    Address = "5.160.74.148",
                    Port = 1409,
                    Protocol = "tcp"
                }
            ],
            ObservedAt = DateTimeOffset.UtcNow
        };
    }

    private sealed record ObservationSourceFixture(
        PathVeerRuntimeObservationSource Source);
}
