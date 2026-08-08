using PathVeer.Core.Configuration;
using PathVeer.Core.CustomRoutes;
using PathVeer.Core.Support;
using PathVeer.Service;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace PathVeer.Service.Tests;

/// <summary>
/// Composition-root tests for the IranDirect Service dependency graph.
///
/// These build the ACTUAL registration graph via
/// <see cref="ServiceCompositionRoot.AddPathVeerServiceComposition"/> — the
/// same call <c>Program.cs</c> makes — with full ServiceProvider validation
/// enabled. A missing or mis-scoped registration fails here exactly as it
/// would when the real service host starts.
///
/// Regression origin: <c>IDesiredConfigurationService</c> had no registration,
/// so <c>SupportSnapshotProvider</c> could not be activated and the host threw
/// during <c>Build()</c> before reaching started state.
/// </summary>
public sealed class ServiceCompositionRootTests : IDisposable
{
    private readonly string _dataDirectory;

    public ServiceCompositionRootTests()
    {
        _dataDirectory = Path.Combine(
            Path.GetTempPath(),
            "IranDirect.CompositionTests",
            Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_dataDirectory);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_dataDirectory))
            {
                Directory.Delete(_dataDirectory, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort temp cleanup; never fail a test on this.
        }
    }

    private ServiceProvider BuildProvider()
    {
        IConfiguration configuration =
            new ConfigurationBuilder()
                .AddInMemoryCollection(
                    new Dictionary<string, string?>())
                .Build();

        ServiceCollection services = new();
        services.AddLogging();
        services.AddPathVeerServiceComposition(
            configuration,
            _dataDirectory);

        // Mirrors the host's own validation: every registered service must be
        // constructible, and scopes must be correct.
        return services.BuildServiceProvider(
            new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true
            });
    }

    [Fact]
    public void Composition_root_builds_and_validates()
    {
        using ServiceProvider provider = BuildProvider();

        Assert.NotNull(provider);
    }

    [Fact]
    public void Desired_configuration_service_resolves_through_interface()
    {
        using ServiceProvider provider = BuildProvider();

        IDesiredConfigurationService resolved =
            provider.GetRequiredService<IDesiredConfigurationService>();

        Assert.NotNull(resolved);
        Assert.IsType<DesiredConfigurationService>(resolved);
    }

    [Fact]
    public void Desired_configuration_interface_shares_the_concrete_singleton()
    {
        using ServiceProvider provider = BuildProvider();

        DesiredConfigurationService concrete =
            provider.GetRequiredService<DesiredConfigurationService>();
        IDesiredConfigurationService viaInterface =
            provider.GetRequiredService<IDesiredConfigurationService>();

        // One instance, therefore one desired-configuration store. A duplicate
        // registration would hand out two objects over the same file.
        Assert.Same(concrete, viaInterface);
    }

    [Fact]
    public void Custom_route_dns_cache_interface_shares_the_concrete_singleton()
    {
        using ServiceProvider provider = BuildProvider();

        CustomRouteDnsCacheService concrete =
            provider.GetRequiredService<CustomRouteDnsCacheService>();
        ICustomRouteDnsCacheService viaInterface =
            provider.GetRequiredService<ICustomRouteDnsCacheService>();

        Assert.Same(concrete, viaInterface);
    }

    [Fact]
    public void Support_snapshot_provider_interface_shares_the_concrete_singleton()
    {
        using ServiceProvider provider = BuildProvider();

        SupportSnapshotProvider concrete =
            provider.GetRequiredService<SupportSnapshotProvider>();
        ISupportSnapshotProvider viaInterface =
            provider.GetRequiredService<ISupportSnapshotProvider>();

        Assert.Same(concrete, viaInterface);
    }

    [Fact]
    public void Support_snapshot_serializer_interface_shares_the_concrete_singleton()
    {
        using ServiceProvider provider = BuildProvider();

        SupportSnapshotSerializer concrete =
            provider.GetRequiredService<SupportSnapshotSerializer>();
        ISupportSnapshotUtf8Serializer viaInterface =
            provider.GetRequiredService<ISupportSnapshotUtf8Serializer>();

        Assert.Same(concrete, viaInterface);
    }

    [Fact]
    public void Support_snapshot_provider_resolves_with_all_dependencies()
    {
        using ServiceProvider provider = BuildProvider();

        // This is the exact activation that failed before the fix.
        SupportSnapshotProvider snapshotProvider =
            provider.GetRequiredService<SupportSnapshotProvider>();

        Assert.NotNull(snapshotProvider);
    }

    [Fact]
    public void Support_bundle_exporter_resolves()
    {
        using ServiceProvider provider = BuildProvider();

        ISupportBundleExporter exporter =
            provider.GetRequiredService<ISupportBundleExporter>();

        Assert.NotNull(exporter);
    }

    [Fact]
    public async Task Host_starts_and_stops_cleanly_with_observability_disabled()
    {
        HostApplicationBuilder builder =
            Host.CreateApplicationBuilder();

        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Observability:Enabled"] = "false"
            });

        builder.Services.AddPathVeerServiceComposition(
            builder.Configuration,
            _dataDirectory);

        using IHost host = builder.Build();

        using CancellationTokenSource cts =
            new(TimeSpan.FromSeconds(30));

        await host.StartAsync(cts.Token);
        await host.StopAsync(cts.Token);
    }
}
