using System.Net;
using IranDirect.Core.CustomRoutes;

namespace IranDirect.Core.Tests.CustomRoutes;

public sealed class CustomRouteResolverTests
{
    [Fact]
    public async Task ResolveAsync_WhenRepositoryEmpty_ReturnsEmpty()
    {
        CustomRouteResolver resolver = CreateResolver();

        CustomRouteResolutionResult result =
            await resolver.ResolveAsync();

        Assert.Empty(result.Prefixes);
        Assert.True(result.AllSucceeded);
    }

    [Fact]
    public async Task ResolveAsync_WhenOnlyDisabledEntries_ReturnsEmpty()
    {
        CustomRouteResolver resolver = CreateResolver(
            CreateEntry(
                CustomRouteEntryType.IpAddress,
                "8.8.8.8",
                enabled: false));

        CustomRouteResolutionResult result =
            await resolver.ResolveAsync();

        Assert.Empty(result.Prefixes);
        Assert.Empty(result.Failures);
    }

    [Fact]
    public async Task ResolveAsync_IpAddress_BecomesHostRoute()
    {
        CustomRouteResolver resolver = CreateResolver(
            CreateEntry(
                CustomRouteEntryType.IpAddress,
                "8.8.8.8"));

        CustomRouteResolutionResult result =
            await resolver.ResolveAsync();

        string prefix = Assert.Single(result.Prefixes);
        Assert.Equal("8.8.8.8/32", prefix);
        Assert.True(result.AllSucceeded);
    }

    [Fact]
    public async Task ResolveAsync_Cidr_PassesThrough()
    {
        CustomRouteResolver resolver = CreateResolver(
            CreateEntry(
                CustomRouteEntryType.Cidr,
                "10.0.0.0/24"));

        CustomRouteResolutionResult result =
            await resolver.ResolveAsync();

        string prefix = Assert.Single(result.Prefixes);
        Assert.Equal("10.0.0.0/24", prefix);
    }

    [Fact]
    public async Task ResolveAsync_Domain_SingleARecord_BecomesHostRoute()
    {
        CustomRouteResolver resolver = CreateResolverWithLookup(
            Lookup(IPAddress.Parse("93.184.216.34")),
            CreateEntry(
                CustomRouteEntryType.Domain,
                "example.com"));

        CustomRouteResolutionResult result =
            await resolver.ResolveAsync();

        string prefix = Assert.Single(result.Prefixes);
        Assert.Equal("93.184.216.34/32", prefix);
        Assert.True(result.AllSucceeded);
    }

    [Fact]
    public async Task ResolveAsync_Domain_MultipleARecords_ProduceOneRouteEach()
    {
        CustomRouteResolver resolver = CreateResolverWithLookup(
            Lookup(
                IPAddress.Parse("93.184.216.34"),
                IPAddress.Parse("93.184.215.14")),
            CreateEntry(
                CustomRouteEntryType.Domain,
                "example.com"));

        CustomRouteResolutionResult result =
            await resolver.ResolveAsync();

        Assert.Equal(
            new[] { "93.184.215.14/32", "93.184.216.34/32" },
            result.Prefixes);
    }

    [Fact]
    public async Task ResolveAsync_Domain_DuplicateARecords_AreDeduplicated()
    {
        CustomRouteResolver resolver = CreateResolverWithLookup(
            Lookup(
                IPAddress.Parse("93.184.216.34"),
                IPAddress.Parse("93.184.216.34")),
            CreateEntry(
                CustomRouteEntryType.Domain,
                "example.com"));

        CustomRouteResolutionResult result =
            await resolver.ResolveAsync();

        string prefix = Assert.Single(result.Prefixes);
        Assert.Equal("93.184.216.34/32", prefix);
    }

    [Fact]
    public async Task ResolveAsync_Domain_Ipv6Records_AreIgnored()
    {
        CustomRouteResolver resolver = CreateResolverWithLookup(
            Lookup(
                IPAddress.Parse("::1"),
                IPAddress.Parse("1.2.3.4")),
            CreateEntry(
                CustomRouteEntryType.Domain,
                "example.com"));

        CustomRouteResolutionResult result =
            await resolver.ResolveAsync();

        string prefix = Assert.Single(result.Prefixes);
        Assert.Equal("1.2.3.4/32", prefix);
    }

    [Fact]
    public async Task ResolveAsync_Domain_OnlyIpv6Records_RecordsFailure()
    {
        CustomRouteResolver resolver = CreateResolverWithLookup(
            Lookup(IPAddress.Parse("::1")),
            CreateEntry(
                CustomRouteEntryType.Domain,
                "example.com"));

        CustomRouteResolutionResult result =
            await resolver.ResolveAsync();

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
        CustomRouteResolver resolver = CreateResolverWithLookup(
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
            await resolver.ResolveAsync();

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
        CustomRouteResolver resolver = CreateResolverWithLookup(
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
            await resolver.ResolveAsync();

        Assert.Equal(
            new[] { "8.8.8.8/32", "10.0.0.0/24" },
            result.Prefixes);
        Assert.True(result.AllSucceeded);
    }

    [Fact]
    public async Task ResolveAsync_ResultIsOrderedByAddressThenPrefixLength()
    {
        CustomRouteResolver resolver = CreateResolver(
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
            await resolver.ResolveAsync();

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
        CustomRouteResolver resolver = CreateResolver(
            CreateEntry(
                CustomRouteEntryType.IpAddress,
                "not-an-ip"));

        CustomRouteResolutionResult result =
            await resolver.ResolveAsync();

        Assert.Empty(result.Prefixes);

        CustomRouteResolutionFailure failure =
            Assert.Single(result.Failures);
        Assert.Equal("not-an-ip", failure.Value);
        Assert.Contains("Invalid IPv4", failure.Reason);
    }

    [Fact]
    public async Task ResolveAsync_Cancellation_IsPropagated()
    {
        CustomRouteResolver resolver = CreateResolverWithLookup(
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
            () => resolver.ResolveAsync(cts.Token));
    }

    private static Func<
        string,
        CancellationToken,
        Task<IReadOnlyList<IPAddress>>> Lookup(
            params IPAddress[] addresses) =>
        (_, _) =>
            Task.FromResult<IReadOnlyList<IPAddress>>(
                addresses);

    private static CustomRouteResolver CreateResolver(
        params CustomRouteEntry[] entries) =>
        CreateResolverWithLookup(
            Lookup(),
            entries);

    private static CustomRouteResolver CreateResolverWithLookup(
        Func<string, CancellationToken, Task<IReadOnlyList<IPAddress>>> lookup,
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

        return new CustomRouteResolver(repository, lookup);
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
        string value,
        bool enabled = true) =>
        new()
        {
            Id = Guid.NewGuid(),
            Type = type,
            Value = value,
            Enabled = enabled,
            CreatedAt = DateTimeOffset.UtcNow,
            ModifiedAt = DateTimeOffset.UtcNow
        };
}
