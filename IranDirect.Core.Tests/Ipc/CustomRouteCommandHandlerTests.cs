using System.Net;
using IranDirect.Core.CustomRoutes;
using IranDirect.Core.Ipc;

namespace IranDirect.Core.Tests.Ipc;

public sealed class CustomRouteCommandHandlerTests
{
    [Fact]
    public async Task ListAsync_WhenEmpty_ReturnsSuccessWithEmptyRoutes()
    {
        Fixture fixture = CreateFixture();

        ServiceResponse response =
            await fixture.Handler.ListAsync();

        Assert.True(response.Success);
        Assert.Empty(response.CustomRoutes);
    }

    [Fact]
    public async Task ListAsync_WhenEntriesExist_ReturnsThem()
    {
        Fixture fixture = CreateFixture();
        await fixture.Service.AddAsync(
            CustomRouteEntryType.Domain,
            "example.com");

        ServiceResponse response =
            await fixture.Handler.ListAsync();

        Assert.True(response.Success);
        CustomRouteEntry entry =
            Assert.Single(response.CustomRoutes);
        Assert.Equal("example.com", entry.Value);
    }

    [Fact]
    public async Task AddAsync_Domain_AddsAndReturnsUpdatedEntries()
    {
        Fixture fixture = CreateFixture();

        ServiceResponse response =
            await fixture.Handler.AddAsync(
                CustomRouteEntryType.Domain,
                "Example.COM.",
                description: "my site");

        Assert.True(response.Success);
        Assert.Contains("Added Domain", response.Message);

        CustomRouteEntry entry =
            Assert.Single(response.CustomRoutes);
        Assert.Equal(
            CustomRouteEntryType.Domain,
            entry.Type);
        Assert.Equal("example.com", entry.Value);
        Assert.True(entry.Enabled);
        Assert.Equal("my site", entry.Description);
    }

    [Fact]
    public async Task AddAsync_Ip_AddsNormalizedEntry()
    {
        Fixture fixture = CreateFixture();

        ServiceResponse response =
            await fixture.Handler.AddAsync(
                CustomRouteEntryType.IpAddress,
                "8.8.8.8");

        Assert.True(response.Success);
        CustomRouteEntry entry =
            Assert.Single(response.CustomRoutes);
        Assert.Equal("8.8.8.8", entry.Value);
    }

    [Fact]
    public async Task AddAsync_Cidr_AddsNormalizedEntry()
    {
        Fixture fixture = CreateFixture();

        ServiceResponse response =
            await fixture.Handler.AddAsync(
                CustomRouteEntryType.Cidr,
                "10.0.0.1/24");

        Assert.True(response.Success);
        CustomRouteEntry entry =
            Assert.Single(response.CustomRoutes);
        Assert.Equal("10.0.0.0/24", entry.Value);
    }

    [Theory]
    [InlineData(CustomRouteEntryType.IpAddress, "not-an-ip")]
    [InlineData(CustomRouteEntryType.Cidr, "10.0.0.0/33")]
    [InlineData(CustomRouteEntryType.Domain, "http://example.com")]
    public async Task AddAsync_InvalidValue_ReturnsValidationError(
        CustomRouteEntryType type,
        string value)
    {
        Fixture fixture = CreateFixture();

        ServiceResponse response =
            await fixture.Handler.AddAsync(type, value);

        Assert.False(response.Success);
        Assert.Equal(
            "INVALID_CUSTOM_ROUTE",
            response.ErrorCode);
        Assert.False(string.IsNullOrWhiteSpace(response.Message));
        Assert.Empty(response.CustomRoutes);
    }

    [Fact]
    public async Task AddAsync_Duplicate_ReturnsDuplicateFailure()
    {
        Fixture fixture = CreateFixture();
        await fixture.Handler.AddAsync(
            CustomRouteEntryType.Domain,
            "example.com");

        ServiceResponse response =
            await fixture.Handler.AddAsync(
                CustomRouteEntryType.Domain,
                "example.com");

        Assert.False(response.Success);
        Assert.Equal(
            "DUPLICATE_CUSTOM_ROUTE",
            response.ErrorCode);
        Assert.Contains(
            "Duplicate custom route",
            response.Message);
    }

    [Fact]
    public async Task SetEnabledAsync_Enable_TogglesEntry()
    {
        Fixture fixture = CreateFixture();
        CustomRouteEntry added = await fixture.Service.AddAsync(
            CustomRouteEntryType.IpAddress,
            "8.8.8.8");
        await fixture.Handler.SetEnabledAsync(
            added.Id.ToString(),
            enabled: false);

        ServiceResponse response =
            await fixture.Handler.SetEnabledAsync(
                added.Id.ToString(),
                enabled: true);

        Assert.True(response.Success);
        Assert.Equal("Custom route enabled.", response.Message);

        CustomRouteEntry entry =
            Assert.Single(response.CustomRoutes);
        Assert.True(entry.Enabled);
    }

    [Fact]
    public async Task SetEnabledAsync_Disable_TogglesEntry()
    {
        Fixture fixture = CreateFixture();
        CustomRouteEntry added = await fixture.Service.AddAsync(
            CustomRouteEntryType.IpAddress,
            "8.8.8.8");

        ServiceResponse response =
            await fixture.Handler.SetEnabledAsync(
                added.Id.ToString(),
                enabled: false);

        Assert.True(response.Success);
        CustomRouteEntry entry =
            Assert.Single(response.CustomRoutes);
        Assert.False(entry.Enabled);
    }

    [Fact]
    public async Task SetEnabledAsync_InvalidId_ReturnsFailure()
    {
        Fixture fixture = CreateFixture();

        ServiceResponse response =
            await fixture.Handler.SetEnabledAsync(
                "not-a-guid",
                enabled: true);

        Assert.False(response.Success);
        Assert.Equal(
            "INVALID_CUSTOM_ROUTE_ID",
            response.ErrorCode);
    }

    [Fact]
    public async Task SetEnabledAsync_UnknownId_ReturnsNotFound()
    {
        Fixture fixture = CreateFixture();

        ServiceResponse response =
            await fixture.Handler.SetEnabledAsync(
                Guid.NewGuid().ToString(),
                enabled: true);

        Assert.False(response.Success);
        Assert.Equal(
            "CUSTOM_ROUTE_NOT_FOUND",
            response.ErrorCode);
    }

    [Fact]
    public async Task RemoveAsync_RemovesEntry()
    {
        Fixture fixture = CreateFixture();
        CustomRouteEntry added = await fixture.Service.AddAsync(
            CustomRouteEntryType.Cidr,
            "10.0.0.0/24");

        ServiceResponse response =
            await fixture.Handler.RemoveAsync(
                added.Id.ToString());

        Assert.True(response.Success);
        Assert.Equal("Custom route removed.", response.Message);
        Assert.Empty(response.CustomRoutes);
    }

    [Fact]
    public async Task RemoveAsync_InvalidId_ReturnsFailure()
    {
        Fixture fixture = CreateFixture();

        ServiceResponse response =
            await fixture.Handler.RemoveAsync("bad-id");

        Assert.False(response.Success);
        Assert.Equal(
            "INVALID_CUSTOM_ROUTE_ID",
            response.ErrorCode);
    }

    [Fact]
    public async Task RemoveAsync_UnknownId_ReturnsNotFound()
    {
        Fixture fixture = CreateFixture();

        ServiceResponse response =
            await fixture.Handler.RemoveAsync(
                Guid.NewGuid().ToString());

        Assert.False(response.Success);
        Assert.Equal(
            "CUSTOM_ROUTE_NOT_FOUND",
            response.ErrorCode);
    }

    [Fact]
    public async Task ResolveAsync_WhenResolvable_ReturnsPrefixes()
    {
        Fixture fixture = CreateFixture();
        await fixture.Service.AddAsync(
            CustomRouteEntryType.Domain,
            "example.com");
        fixture.Dns = (host, _) =>
            Task.FromResult<IReadOnlyList<IPAddress>>(
                [IPAddress.Parse("93.184.216.34")]);

        ServiceResponse response =
            await fixture.Handler.ResolveAsync();

        Assert.True(response.Success);
        Assert.NotNull(response.CustomRouteResolution);
        Assert.Equal(
            ["93.184.216.34/32"],
            response.CustomRouteResolution.Prefixes);
        Assert.True(
            response.CustomRouteResolution.AllSucceeded);
    }

    [Fact]
    public async Task ResolveAsync_WhenLookupFails_ReturnsFailures()
    {
        Fixture fixture = CreateFixture();
        await fixture.Service.AddAsync(
            CustomRouteEntryType.Domain,
            "broken.example");
        fixture.Dns = (host, _) =>
            throw new InvalidOperationException("NXDOMAIN");

        ServiceResponse response =
            await fixture.Handler.ResolveAsync();

        Assert.True(response.Success);
        Assert.NotNull(response.CustomRouteResolution);

        CustomRouteResolutionResult result =
            response.CustomRouteResolution;
        Assert.Empty(result.Prefixes);
        Assert.False(result.AllSucceeded);

        CustomRouteResolutionFailure failure =
            Assert.Single(result.Failures);
        Assert.Equal("broken.example", failure.Value);
    }

    private sealed class Fixture
    {
        public required CustomRouteRepository Repository { get; init; }

        public required CustomRouteService Service { get; init; }

        public CustomRouteCommandHandler Handler { get; set; } = null!;

        public required Func<
            string,
            CancellationToken,
            Task<IReadOnlyList<IPAddress>>> Dns { get; set; }
    }

    private static Fixture CreateFixture()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "IranDirect.Tests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(directory);

        CustomRouteRepository repository = new(
            new CustomRouteStore(
                Path.Combine(
                    directory,
                    "custom-routes.json")));

        CustomRouteService service = new(
            repository,
            new CustomRouteEntryValidator());

        Fixture fixture = new()
        {
            Repository = repository,
            Service = service,
            Dns = (_, _) =>
                Task.FromResult<IReadOnlyList<IPAddress>>([])
        };

        CustomRouteResolver resolver = new(
            repository,
            (host, token) => fixture.Dns(host, token));

        fixture.Handler =
            new CustomRouteCommandHandler(service, resolver);

        return fixture;
    }
}
