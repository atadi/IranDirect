using IranDirect.Core;
using IranDirect.Core.Configuration;
using IranDirect.Core.CustomRoutes;
using IranDirect.Core.Diagnostics;
using IranDirect.Core.Diagnostics.Configuration;
using IranDirect.Core.Diagnostics.Routing;
using IranDirect.Core.Diagnostics.Runtime;
using IranDirect.Core.Ipc;
using IranDirect.Core.Networking;
using IranDirect.Core.Observability;
using IranDirect.Core.Observability.Telemetry;
using IranDirect.Core.Planning;
using IranDirect.Core.Prefixes;
using IranDirect.Core.Routing;
using IranDirect.Core.Runtime;
using IranDirect.Core.Runtime.Execution;
using IranDirect.Core.Runtime.Profiling;
using IranDirect.Core.Runtime.Reconciliation;
using IranDirect.Core.State;
using IranDirect.Core.Support;
using IranDirect.Core.SystemTools;
using IranDirect.Core.Vpn;
using IranDirect.Service.Ipc;
using IranDirect.Service.Operations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IranDirect.Service;

/// <summary>
/// The IranDirect Service composition root.
///
/// This is the single definition of the Service's dependency graph. Both
/// <c>Program.cs</c> and the Service composition tests call
/// <see cref="AddIranDirectServiceComposition"/>, so a registration defect
/// cannot exist in the shipped host without also failing the tests.
///
/// Host-specific concerns (Windows Service lifetime, telemetry export
/// hosting) stay in <c>Program.cs</c>; only the application graph lives here.
/// </summary>
public static class ServiceCompositionRoot
{
    /// <summary>
    /// Registers the full IranDirect Service dependency graph.
    /// </summary>
    /// <param name="services">The service collection to populate.</param>
    /// <param name="configuration">Host configuration.</param>
    /// <param name="dataDirectory">
    /// Directory holding IranDirect state files. The caller owns creating it.
    /// </param>
    public static IServiceCollection AddIranDirectServiceComposition(
        this IServiceCollection services,
        IConfiguration configuration,
        string dataDirectory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);

        services.AddHttpClient<OfficialIranPrefixSource>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "IranDirect/1.0");
        });
        services.AddSingleton<IPrefixSource>(
            serviceProvider =>
                serviceProvider.GetRequiredService<
                    OfficialIranPrefixSource>());

        services.AddSingleton(
            new PrefixFileRepository(
                Path.Combine(
                    dataDirectory,
                    "iran-ipv4-prefixes.txt")));

        services.AddSingleton(
            new PrefixSourceMetadataStore(
                Path.Combine(
                    dataDirectory,
                    "prefix-source-metadata.json")));
        services.AddSingleton<
            PrefixSourceMetadataValidator>();
        services.AddSingleton<
            IPrefixSourceMetadataRepository>(
            serviceProvider =>
                new PrefixSourceMetadataRepository(
                    serviceProvider.GetRequiredService<
                        PrefixSourceMetadataStore>(),
                    serviceProvider.GetRequiredService<
                        PrefixSourceMetadataValidator>()));
        services.AddSingleton<
            IPrefixSourceMetadataService>(
            serviceProvider =>
                new PrefixSourceMetadataService(
                    serviceProvider.GetRequiredService<
                        IPrefixSourceMetadataRepository>(),
                    serviceProvider.GetRequiredService<
                        TimeProvider>()));

        PrefixUpdateCheckOptions prefixUpdateCheckOptions = new();
        configuration.GetSection(
            "PrefixUpdateCheck").Bind(prefixUpdateCheckOptions);
        PrefixUpdateCheckOptions.Validate(prefixUpdateCheckOptions);
        services.AddSingleton(prefixUpdateCheckOptions);

        services.AddHttpClient<
            OfficialIranPrefixUpdateChecker>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "IranDirect/1.0");
        });
        services.AddSingleton<IPrefixUpdateChecker>(
            serviceProvider =>
                serviceProvider.GetRequiredService<
                    OfficialIranPrefixUpdateChecker>());

        PrefixUpdateMonitorOptions prefixUpdateMonitorOptions =
            new();
        configuration.GetSection(
            "PrefixUpdateMonitor").Bind(prefixUpdateMonitorOptions);
        PrefixUpdateMonitorOptions.Validate(
            prefixUpdateMonitorOptions);
        services.AddSingleton(
            prefixUpdateMonitorOptions);

        services.AddSingleton<PrefixUpdateMonitor>();
        services.AddSingleton<IPrefixUpdateMonitor>(
            serviceProvider =>
                serviceProvider.GetRequiredService<
                    PrefixUpdateMonitor>());
        services.AddHostedService<
            PrefixUpdateMonitorHostedService>();

        PrefixSourceHistoryOptions historyOptions = new();
        configuration.GetSection(
            "PrefixSourceHistory").Bind(historyOptions);
        PrefixSourceHistoryOptions.Validate(historyOptions);
        services.AddSingleton(historyOptions);

        services.AddSingleton(
            new PrefixSourceUpdateHistoryStore(
                Path.Combine(
                    dataDirectory,
                    "prefix-source-update-history.json")));
        services.AddSingleton<
            PrefixSourceUpdateHistoryValidator>();
        services.AddSingleton<
            IPrefixSourceUpdateHistoryRepository>(
            serviceProvider =>
                new PrefixSourceUpdateHistoryRepository(
                    serviceProvider.GetRequiredService<
                        PrefixSourceUpdateHistoryStore>(),
                    serviceProvider.GetRequiredService<
                        PrefixSourceUpdateHistoryValidator>(),
                    serviceProvider.GetRequiredService<
                        PrefixSourceHistoryOptions>()));
        services.AddSingleton<
            IPrefixSourceUpdateHistoryService>(
            serviceProvider =>
                new PrefixSourceUpdateHistoryService(
                    serviceProvider.GetRequiredService<
                        IPrefixSourceUpdateHistoryRepository>(),
                    serviceProvider.GetRequiredService<
                        PrefixSourceHistoryOptions>(),
                    serviceProvider.GetRequiredService<
                        TimeProvider>()));

        services.AddSingleton(
            new StateRepository(
                Path.Combine(
                    dataDirectory,
                    "state.json")));

        RouteInventoryStore routeInventoryStore = new(
            Path.Combine(dataDirectory, "route-inventory.json"));
        services.AddSingleton(routeInventoryStore);
        services.AddSingleton<IRouteInventoryPersistence>(routeInventoryStore);

        VpnEndpointInventoryStore endpointInventoryStore = new(
            Path.Combine(dataDirectory, "endpoint-inventory.json"));
        services.AddSingleton(endpointInventoryStore);
        services.AddSingleton<IEndpointInventoryPersistence>(endpointInventoryStore);

        // Durable write-ahead journal for crash-consistent route-ownership
        // recovery. Survives process death between a native mutation and the
        // authoritative inventory persist, so recovery can prove IranDirect
        // initiated an interrupted mutation instead of guessing from route shape.
        RouteMutationJournalStore routeMutationJournalStore = new(
            Path.Combine(dataDirectory, "route-mutation-journal.json"));
        services.AddSingleton(routeMutationJournalStore);
        services.AddSingleton<IRouteMutationJournal>(routeMutationJournalStore);

        services.AddSingleton<RouteMutationRecovery>();

        services.AddSingleton<DesiredConfigurationValidator>();
        services.AddSingleton(
            serviceProvider =>
                new DesiredConfigurationStore(
                    Path.Combine(
                        dataDirectory,
                        "desired-configuration.json"),
                    serviceProvider.GetRequiredService<
                        DesiredConfigurationValidator>()));
        services.AddSingleton<DesiredConfigurationService>();

        // DesiredConfigurationService is consumed through its interface by
        // SupportSnapshotProvider. Forward to the existing singleton rather
        // than registering the concrete type twice, so both the concrete and
        // interface resolutions share one instance (and therefore one store).
        services.AddSingleton<IDesiredConfigurationService>(
            serviceProvider =>
                serviceProvider.GetRequiredService<
                    DesiredConfigurationService>());

        services.AddSingleton(
            new CustomRouteStore(
                Path.Combine(
                    dataDirectory,
                    "custom-routes.json")));
        services.AddSingleton<ICustomRouteRepository>(
            serviceProvider =>
                new CustomRouteRepository(
                    serviceProvider.GetRequiredService<
                        CustomRouteStore>()));
        services.AddSingleton<CustomRouteEntryValidator>();
        services.AddSingleton<CustomRouteService>();

        CustomRouteDnsCacheStore customRouteDnsCacheStore = new(
            Path.Combine(
                dataDirectory,
                "custom-route-dns-cache.json"));
        services.AddSingleton(customRouteDnsCacheStore);
        services.AddSingleton<ICustomRouteDnsCacheRepository>(
            serviceProvider =>
                new CustomRouteDnsCacheRepository(
                    customRouteDnsCacheStore,
                    serviceProvider.GetRequiredService<TimeProvider>()));

        CustomRouteDnsCacheOptions dnsCacheOptions = new();
        configuration.GetSection("CustomRoutes").Bind(dnsCacheOptions);
        CustomRouteDnsCacheOptions.Validate(dnsCacheOptions);
        services.AddSingleton(dnsCacheOptions);

        services.AddSingleton<ICustomRouteResolver, CustomRouteResolver>();
        services.AddSingleton<CustomRouteDnsCacheService>();

        // Same instance-sharing rule as IDesiredConfigurationService: the
        // cache service is stateful, so the interface must resolve to the
        // already-registered singleton.
        services.AddSingleton<ICustomRouteDnsCacheService>(
            serviceProvider =>
                serviceProvider.GetRequiredService<
                    CustomRouteDnsCacheService>());

        services.AddSingleton<CustomRouteCommandHandler>();
        services.AddSingleton<OpenVpnProfileParser>();
        services.AddSingleton<VpnEndpointResolver>();
        services.AddSingleton(
            serviceProvider =>
                new OpenVpnEndpointProvider(
                    Path.Combine(
                        dataDirectory,
                        "vpn-profile.ovpn"),
                    serviceProvider.GetRequiredService<
                        OpenVpnProfileParser>(),
                    serviceProvider.GetRequiredService<
                        VpnEndpointResolver>()));

        services.AddSingleton<GatewayDetector>();
        services.AddSingleton<CommandRunner>();

        // The native route boundary (WindowsRouteApi -> powershell.exe /
        // netsh.exe) is wrapped with the telemetry decorator so every
        // enumerate/create/delete system call emits one Routes.* activity and
        // one set of route-operation measurements. WindowsRouteApi stays
        // native-only; the decorator lives in the Observability.Telemetry
        // namespace and is a legitimate composition-root seam.
        services.AddSingleton<IRouteManager>(serviceProvider =>
        {
            var commandRunner =
                serviceProvider.GetRequiredService<CommandRunner>();
            var windowsApi = new WindowsRouteApi(commandRunner);
            var telemetryApi = new TelemetryRouteApi(windowsApi);
            return new WindowsRouteManager(commandRunner, telemetryApi);
        });

        services.AddSingleton<VpnEndpointRouteManager>();
        services.AddSingleton<IranDirectController>();
        services.AddSingleton<IIranDirectStatusProvider>(
            serviceProvider =>
                serviceProvider.GetRequiredService<IranDirectController>());

        services.AddSingleton<
            DesiredConfigurationDiagnosticCheck>();
        services.AddSingleton<
            PrefixConfigurationDiagnosticCheck>();
        services.AddSingleton<
            PrefixMetadataDiagnosticCheck>();
        services.AddSingleton<
            PrefixHistoryDiagnosticCheck>();
        services.AddSingleton<
            CustomRoutesDiagnosticCheck>();
        services.AddSingleton<
            RuntimeStateDiagnosticCheck>();
        services.AddSingleton<
            RuntimeOperationDiagnosticCheck>();
        services.AddSingleton<
            RouteInventoryDiagnosticCheck>();
        services.AddSingleton<
            RuntimeSnapshotDiagnosticCheck>();
        services.AddSingleton<
            WindowsRouteTableDiagnosticCheck>();
        services.AddSingleton<
            RouteOwnershipDiagnosticCheck>();
        services.AddSingleton<
            ManagedRouteConsistencyDiagnosticCheck>();
        services.AddSingleton<IDiagnosticRunner>(
            serviceProvider =>
                new DiagnosticRunner(
                    new List<IDiagnosticCheck>
                    {
                        serviceProvider.GetRequiredService<
                            DesiredConfigurationDiagnosticCheck>(),
                        serviceProvider.GetRequiredService<
                            PrefixConfigurationDiagnosticCheck>(),
                        serviceProvider.GetRequiredService<
                            PrefixMetadataDiagnosticCheck>(),
                        serviceProvider.GetRequiredService<
                            PrefixHistoryDiagnosticCheck>(),
                        serviceProvider.GetRequiredService<
                            CustomRoutesDiagnosticCheck>(),
                        serviceProvider.GetRequiredService<
                            RuntimeStateDiagnosticCheck>(),
                        serviceProvider.GetRequiredService<
                            RuntimeOperationDiagnosticCheck>(),
                        serviceProvider.GetRequiredService<
                            RouteInventoryDiagnosticCheck>(),
                        serviceProvider.GetRequiredService<
                            RuntimeSnapshotDiagnosticCheck>(),
                        serviceProvider.GetRequiredService<
                            WindowsRouteTableDiagnosticCheck>(),
                        serviceProvider.GetRequiredService<
                            RouteOwnershipDiagnosticCheck>(),
                        serviceProvider.GetRequiredService<
                            ManagedRouteConsistencyDiagnosticCheck>()
                    }));

        services.AddSingleton(
            serviceProvider =>
                new IranDirectDiagnosticsService(
                    Path.Combine(
                        dataDirectory,
                        "vpn-profile.ovpn"),
                    serviceProvider.GetRequiredService<
                        PrefixFileRepository>(),
                    serviceProvider.GetRequiredService<
                        StateRepository>(),
                    serviceProvider.GetRequiredService<
                        RouteInventoryStore>(),
                    serviceProvider.GetRequiredService<
                        VpnEndpointInventoryStore>(),
                    serviceProvider.GetRequiredService<
                        GatewayDetector>(),
                    serviceProvider.GetRequiredService<
                        OpenVpnEndpointProvider>(),
                    serviceProvider.GetRequiredService<
                        VpnEndpointRouteManager>()));
        services.AddSingleton<IRuntimeObservationSource>(
            serviceProvider =>
                new IranDirectRuntimeObservationSource(
                    Path.Combine(
                        dataDirectory,
                        "vpn-profile.ovpn"),
                    serviceProvider.GetRequiredService<
                        OpenVpnEndpointProvider>(),
                    serviceProvider.GetRequiredService<
                        GatewayDetector>(),
                    serviceProvider.GetRequiredService<
                        PrefixFileRepository>(),
                    serviceProvider.GetRequiredService<
                        IRouteManager>(),
                    customRouteResolver:
                        serviceProvider.GetRequiredService<
                            ICustomRouteResolver>()));

        services.AddSingleton<RuntimeObserver>();
        services.AddSingleton<RuntimePlanner>();
        services.AddSingleton<RuntimeCoordinator>();
        services.AddSingleton<IRuntimePlanCoordinator>(
            sp => sp.GetRequiredService<RuntimeCoordinator>());
        services.AddSingleton<
            IRuntimeRouteOwnershipSource,
            InventoryRouteOwnershipSource>();
        services.AddSingleton<RuntimeRouteOwnershipProvider>();
        services.AddSingleton<RuntimeChangeSetPlanner>();
        services.AddSingleton<IRuntimeChangeSetPlanner>(
            sp => sp.GetRequiredService<RuntimeChangeSetPlanner>());
        services.AddSingleton<
            IRuntimeReconciler,
            RuntimeReconciler>();
        services.AddSingleton<RuntimeExecutionPlanner>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<
            IRuntimeDecisionBuilder, RuntimeDecisionBuilder>();
        services.AddSingleton<IRuntimeExecutionStepHandler>(
            serviceProvider =>
                new WindowsRuntimeExecutionStepHandler(
                    serviceProvider.GetRequiredService<IRouteManager>(),
                    serviceProvider.GetRequiredService<IRouteInventoryPersistence>(),
                    serviceProvider.GetRequiredService<IEndpointInventoryPersistence>(),
                    serviceProvider.GetRequiredService<RuntimeCycleProfiler>(),
                    serviceProvider.GetRequiredService<IRouteMutationJournal>()));
        services.AddSingleton<
            IRuntimeExecutor, RuntimeExecutor>();
        services.AddSingleton<RuntimeOperationStatus>();
        services.AddSingleton(
            _ => new RuntimePerfReportStore(
                RuntimePerfReportStore.DefaultDirectory));
        services.AddSingleton(
            serviceProvider => new RuntimeCycleProfiler(
                configuration.GetValue(
                    "Profiling:Enabled", defaultValue: true),
                serviceProvider.GetRequiredService<
                    RuntimePerfReportStore>()));
        services.AddSingleton<RuntimeCycleCoordinator>();
        services.AddSingleton<OperationCoordinator>();
        services.AddSingleton<
            IRuntimeSnapshotProvider, RuntimeSnapshotProvider>();
        services.AddSingleton<RuntimeSnapshotCommandHandler>();
        services.AddSingleton<
            PrefixUpdateCheckCommandHandler>();
        services.AddSingleton<DiagnosticCommandHandler>();
        services.AddSingleton<
            IExecutionPreviewBuilder,
            ExecutionPreviewBuilder>();
        services.AddSingleton<
            IRuntimePreviewPlanner, RuntimePreviewPlanner>();
        services.AddSingleton<
            ExecutionPreviewCommandHandler>();
        services.AddSingleton<SupportSnapshotSerializer>();

        // SupportSnapshotExporter consumes the serializer through its
        // interface; forward to the registered singleton.
        services.AddSingleton<ISupportSnapshotUtf8Serializer>(
            serviceProvider =>
                serviceProvider.GetRequiredService<
                    SupportSnapshotSerializer>());

        services.AddSingleton<SupportSnapshotProvider>();

        // SupportSnapshotExporter consumes the provider through its
        // interface; forward to the registered singleton.
        services.AddSingleton<ISupportSnapshotProvider>(
            serviceProvider =>
                serviceProvider.GetRequiredService<
                    SupportSnapshotProvider>());

        services.AddSingleton<SupportSnapshotExporter>();
        services.AddSingleton<ISupportBundleExporter>(
            serviceProvider =>
                new SupportBundleExporter(
                    serviceProvider.GetRequiredService<
                        SupportSnapshotExporter>()));
        services.AddSingleton<SupportBundleCommandHandler>();
        services.AddSingleton<NamedPipeCommandServer>();
        services.AddHostedService<IranDirectWorker>();

        return services;
    }
}
