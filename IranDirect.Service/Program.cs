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
using IranDirect.Core.Prefixes;
using IranDirect.Core.Routing;
using IranDirect.Core.Runtime;
using IranDirect.Core.Runtime.Execution;
using IranDirect.Core.Runtime.Profiling;
using IranDirect.Core.Runtime.Reconciliation;
using IranDirect.Core.State;
using IranDirect.Core.SystemTools;
using IranDirect.Core.Vpn;
using IranDirect.Service;
using IranDirect.Service.Ipc;
using IranDirect.Service.Operations;

string dataDirectory = Path.Combine(
    Environment.GetFolderPath(
        Environment.SpecialFolder.CommonApplicationData),
    "IranDirect");

Directory.CreateDirectory(dataDirectory);

HostApplicationBuilder builder =
    Host.CreateApplicationBuilder(args);

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "IranDirect Service";
});

builder.Services.AddHttpClient<OfficialIranPrefixSource>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.UserAgent.ParseAdd(
        "IranDirect/1.0");
});
builder.Services.AddSingleton<IPrefixSource>(
    serviceProvider =>
        serviceProvider.GetRequiredService<
            OfficialIranPrefixSource>());

builder.Services.AddSingleton(
    new PrefixFileRepository(
        Path.Combine(
            dataDirectory,
            "iran-ipv4-prefixes.txt")));

builder.Services.AddSingleton(
    new PrefixSourceMetadataStore(
        Path.Combine(
            dataDirectory,
            "prefix-source-metadata.json")));
builder.Services.AddSingleton<
    PrefixSourceMetadataValidator>();
builder.Services.AddSingleton<
    IPrefixSourceMetadataRepository>(
    serviceProvider =>
        new PrefixSourceMetadataRepository(
            serviceProvider.GetRequiredService<
                PrefixSourceMetadataStore>(),
            serviceProvider.GetRequiredService<
                PrefixSourceMetadataValidator>()));
builder.Services.AddSingleton<
    IPrefixSourceMetadataService>(
    serviceProvider =>
        new PrefixSourceMetadataService(
            serviceProvider.GetRequiredService<
                IPrefixSourceMetadataRepository>(),
            serviceProvider.GetRequiredService<
                TimeProvider>()));

PrefixUpdateCheckOptions prefixUpdateCheckOptions = new();
builder.Configuration.GetSection(
    "PrefixUpdateCheck").Bind(prefixUpdateCheckOptions);
PrefixUpdateCheckOptions.Validate(prefixUpdateCheckOptions);
builder.Services.AddSingleton(prefixUpdateCheckOptions);

builder.Services.AddHttpClient<
    OfficialIranPrefixUpdateChecker>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.UserAgent.ParseAdd(
        "IranDirect/1.0");
});
builder.Services.AddSingleton<IPrefixUpdateChecker>(
    serviceProvider =>
        serviceProvider.GetRequiredService<
            OfficialIranPrefixUpdateChecker>());

PrefixUpdateMonitorOptions prefixUpdateMonitorOptions =
    new();
builder.Configuration.GetSection(
    "PrefixUpdateMonitor").Bind(prefixUpdateMonitorOptions);
PrefixUpdateMonitorOptions.Validate(
    prefixUpdateMonitorOptions);
builder.Services.AddSingleton(
    prefixUpdateMonitorOptions);

builder.Services.AddSingleton<PrefixUpdateMonitor>();
builder.Services.AddSingleton<IPrefixUpdateMonitor>(
    serviceProvider =>
        serviceProvider.GetRequiredService<
            PrefixUpdateMonitor>());
builder.Services.AddHostedService<
    PrefixUpdateMonitorHostedService>();

PrefixSourceHistoryOptions historyOptions = new();
builder.Configuration.GetSection(
    "PrefixSourceHistory").Bind(historyOptions);
PrefixSourceHistoryOptions.Validate(historyOptions);
builder.Services.AddSingleton(historyOptions);

builder.Services.AddSingleton(
    new PrefixSourceUpdateHistoryStore(
        Path.Combine(
            dataDirectory,
            "prefix-source-update-history.json")));
builder.Services.AddSingleton<
    PrefixSourceUpdateHistoryValidator>();
builder.Services.AddSingleton<
    IPrefixSourceUpdateHistoryRepository>(
    serviceProvider =>
        new PrefixSourceUpdateHistoryRepository(
            serviceProvider.GetRequiredService<
                PrefixSourceUpdateHistoryStore>(),
            serviceProvider.GetRequiredService<
                PrefixSourceUpdateHistoryValidator>(),
            serviceProvider.GetRequiredService<
                PrefixSourceHistoryOptions>()));
builder.Services.AddSingleton<
    IPrefixSourceUpdateHistoryService>(
    serviceProvider =>
        new PrefixSourceUpdateHistoryService(
            serviceProvider.GetRequiredService<
                IPrefixSourceUpdateHistoryRepository>(),
            serviceProvider.GetRequiredService<
                PrefixSourceHistoryOptions>(),
            serviceProvider.GetRequiredService<
                TimeProvider>()));

builder.Services.AddSingleton(
    new StateRepository(
        Path.Combine(
            dataDirectory,
            "state.json")));

RouteInventoryStore routeInventoryStore = new(
    Path.Combine(dataDirectory, "route-inventory.json"));
builder.Services.AddSingleton(routeInventoryStore);
builder.Services.AddSingleton<IRouteInventoryPersistence>(routeInventoryStore);

VpnEndpointInventoryStore endpointInventoryStore = new(
    Path.Combine(dataDirectory, "endpoint-inventory.json"));
builder.Services.AddSingleton(endpointInventoryStore);
builder.Services.AddSingleton<IEndpointInventoryPersistence>(endpointInventoryStore);

builder.Services.AddSingleton<DesiredConfigurationValidator>();
builder.Services.AddSingleton(
    serviceProvider =>
        new DesiredConfigurationStore(
            Path.Combine(
                dataDirectory,
                "desired-configuration.json"),
            serviceProvider.GetRequiredService<
                DesiredConfigurationValidator>()));
builder.Services.AddSingleton<DesiredConfigurationService>();
builder.Services.AddSingleton(
    new CustomRouteStore(
        Path.Combine(
            dataDirectory,
            "custom-routes.json")));
builder.Services.AddSingleton<ICustomRouteRepository>(
    serviceProvider =>
        new CustomRouteRepository(
            serviceProvider.GetRequiredService<
                CustomRouteStore>()));
builder.Services.AddSingleton<CustomRouteEntryValidator>();
builder.Services.AddSingleton<CustomRouteService>();

CustomRouteDnsCacheStore customRouteDnsCacheStore = new(
    Path.Combine(
        dataDirectory,
        "custom-route-dns-cache.json"));
builder.Services.AddSingleton(customRouteDnsCacheStore);
builder.Services.AddSingleton<ICustomRouteDnsCacheRepository>(
    serviceProvider =>
        new CustomRouteDnsCacheRepository(
            customRouteDnsCacheStore,
            serviceProvider.GetRequiredService<TimeProvider>()));

CustomRouteDnsCacheOptions dnsCacheOptions = new();
builder.Configuration.GetSection("CustomRoutes").Bind(dnsCacheOptions);
CustomRouteDnsCacheOptions.Validate(dnsCacheOptions);
builder.Services.AddSingleton(dnsCacheOptions);

builder.Services.AddSingleton<ICustomRouteResolver, CustomRouteResolver>();
builder.Services.AddSingleton<CustomRouteDnsCacheService>();
builder.Services.AddSingleton<CustomRouteCommandHandler>();
builder.Services.AddSingleton<OpenVpnProfileParser>();
builder.Services.AddSingleton<VpnEndpointResolver>();
builder.Services.AddSingleton(
    serviceProvider =>
        new OpenVpnEndpointProvider(
            Path.Combine(
                dataDirectory,
                "vpn-profile.ovpn"),
            serviceProvider.GetRequiredService<
                OpenVpnProfileParser>(),
            serviceProvider.GetRequiredService<
                VpnEndpointResolver>()));

builder.Services.AddSingleton<GatewayDetector>();
builder.Services.AddSingleton<CommandRunner>();

builder.Services.AddSingleton<
    IRouteManager,
    WindowsRouteManager>();

builder.Services.AddSingleton<VpnEndpointRouteManager>();
builder.Services.AddSingleton<IranDirectController>();
builder.Services.AddSingleton<IIranDirectStatusProvider>(
    serviceProvider =>
        serviceProvider.GetRequiredService<IranDirectController>());

builder.Services.AddSingleton<
    DesiredConfigurationDiagnosticCheck>();
builder.Services.AddSingleton<
    PrefixConfigurationDiagnosticCheck>();
builder.Services.AddSingleton<
    PrefixMetadataDiagnosticCheck>();
builder.Services.AddSingleton<
    PrefixHistoryDiagnosticCheck>();
builder.Services.AddSingleton<
    CustomRoutesDiagnosticCheck>();
builder.Services.AddSingleton<
    RuntimeStateDiagnosticCheck>();
builder.Services.AddSingleton<
    RuntimeOperationDiagnosticCheck>();
builder.Services.AddSingleton<
    RouteInventoryDiagnosticCheck>();
builder.Services.AddSingleton<
    RuntimeSnapshotDiagnosticCheck>();
builder.Services.AddSingleton<
    WindowsRouteTableDiagnosticCheck>();
builder.Services.AddSingleton<
    RouteOwnershipDiagnosticCheck>();
builder.Services.AddSingleton<
    ManagedRouteConsistencyDiagnosticCheck>();
builder.Services.AddSingleton<IDiagnosticRunner>(
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

builder.Services.AddSingleton(
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
builder.Services.AddSingleton<IRuntimeObservationSource>(
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

builder.Services.AddSingleton<RuntimeObserver>();
builder.Services.AddSingleton<RuntimePlanner>();
builder.Services.AddSingleton<RuntimeCoordinator>();
builder.Services.AddSingleton<IRuntimePlanCoordinator>(
    sp => sp.GetRequiredService<RuntimeCoordinator>());
builder.Services.AddSingleton<
    IRuntimeRouteOwnershipSource,
    InventoryRouteOwnershipSource>();
builder.Services.AddSingleton<RuntimeRouteOwnershipProvider>();
builder.Services.AddSingleton<RuntimeChangeSetPlanner>();
builder.Services.AddSingleton<
    IRuntimeReconciler,
    RuntimeReconciler>();
builder.Services.AddSingleton<RuntimeExecutionPlanner>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<
    IRuntimeDecisionBuilder, RuntimeDecisionBuilder>();
builder.Services.AddSingleton<
    IRuntimeExecutionStepHandler,
    WindowsRuntimeExecutionStepHandler>();
builder.Services.AddSingleton<
    IRuntimeExecutor, RuntimeExecutor>();
builder.Services.AddSingleton<RuntimeOperationStatus>();
builder.Services.AddSingleton(
    _ => new RuntimePerfReportStore(
        RuntimePerfReportStore.DefaultDirectory));
builder.Services.AddSingleton(
    serviceProvider => new RuntimeCycleProfiler(
        builder.Configuration.GetValue(
            "Profiling:Enabled", defaultValue: true),
        serviceProvider.GetRequiredService<
            RuntimePerfReportStore>()));
builder.Services.AddSingleton<RuntimeCycleCoordinator>();
builder.Services.AddSingleton<OperationCoordinator>();
builder.Services.AddSingleton<
    IRuntimeSnapshotProvider, RuntimeSnapshotProvider>();
builder.Services.AddSingleton<RuntimeSnapshotCommandHandler>();
builder.Services.AddSingleton<
    PrefixUpdateCheckCommandHandler>();
builder.Services.AddSingleton<NamedPipeCommandServer>();
builder.Services.AddHostedService<IranDirectWorker>();

IHost host = builder.Build();

await host.RunAsync();