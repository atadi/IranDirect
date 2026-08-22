using PathVeer.Core.Configuration;
using PathVeer.Core.CustomRoutes;
using PathVeer.Core.Persistence;
using PathVeer.Core.State;
using PathVeer.Core.Support;
using PathVeer.Service;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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
    public async Task Composition_wires_persistence_recovery_diagnostics()
    {
        // Proves the REAL production DI graph (AddPathVeerServiceComposition)
        // wires JsonStoreRecoveryOptions.OnRecovery into the Service logger for
        // the BackupRollback stores, not merely an internal repository test.
        ServiceCollection services = new();
        CapturingLoggerProvider capture = new();
        services.AddLogging(builder => builder.AddProvider(capture));
        services.AddPathVeerServiceComposition(
            new ConfigurationBuilder()
                .AddInMemoryCollection(
                    new Dictionary<string, string?>())
                .Build(),
            _dataDirectory);

        using ServiceProvider provider = services.BuildServiceProvider(
            new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true
            });

        // Resolve the production StateRepository and point it at a corrupt
        // state.json with NO backup in THIS test's own temp data directory
        // (never the real C:\ProgramData). The wired recovery diagnostic must
        // fire a failure event (and must NOT silently default).
        StateRepository stateRepository =
            provider.GetRequiredService<StateRepository>();

        string statePath =
            Path.Combine(_dataDirectory, "state.json");
        await File.WriteAllBytesAsync(statePath, new byte[271]); // all-NUL corrupt

        await Assert.ThrowsAsync<PersistenceCorruptException>(
            () => stateRepository.LoadAsync());

        // The production-wired logger captured a recovery event for this store.
        Assert.Contains(
            capture.Entries,
            e => e.Category.Contains(nameof(StateRepository))
                 && (e.Level == LogLevel.Error || e.Level == LogLevel.Information)
                 && e.Message.Contains("PersistenceRecovery"));
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public List<LogEntry> Entries { get; } = [];

        public ILogger CreateLogger(string categoryName) =>
            new CapturingLogger(categoryName, Entries);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger : ILogger
        {
            private readonly string _category;
            private readonly List<LogEntry> _entries;

            public CapturingLogger(string category, List<LogEntry> entries)
            {
                _category = category;
                _entries = entries;
            }

            public IDisposable? BeginScope<TState>(
                TState state)
                where TState : notnull =>
                null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                _entries.Add(new LogEntry(
                    _category,
                    logLevel,
                    formatter(state, exception)));
            }
        }
    }

    private sealed record LogEntry(string Category, LogLevel Level, string Message);

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
