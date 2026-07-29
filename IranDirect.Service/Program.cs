using IranDirect.Core;
using IranDirect.Core.Configuration;
using IranDirect.Core.Diagnostics;
using IranDirect.Core.Networking;
using IranDirect.Core.Prefixes;
using IranDirect.Core.Routing;
using IranDirect.Core.Runtime;
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

builder.Services.AddHttpClient<IranPrefixProvider>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.UserAgent.ParseAdd(
        "IranDirect/1.0");
});

builder.Services.AddSingleton(
    new PrefixFileRepository(
        Path.Combine(
            dataDirectory,
            "iran-ipv4-prefixes.txt")));

builder.Services.AddSingleton(
    new StateRepository(
        Path.Combine(
            dataDirectory,
            "state.json")));

builder.Services.AddSingleton(
    new RouteInventoryStore(
        Path.Combine(
            dataDirectory,
            "route-inventory.json")));

builder.Services.AddSingleton(
    new VpnEndpointInventoryStore(
        Path.Combine(
            dataDirectory,
            "endpoint-inventory.json")));

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

builder.Services.AddSingleton<RouteReconciler>();
builder.Services.AddSingleton<VpnEndpointRouteManager>();
builder.Services.AddSingleton<IranDirectController>();

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
                IRouteManager>()));

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
builder.Services.AddSingleton<RuntimeCycleCoordinator>();
builder.Services.AddSingleton<OperationCoordinator>();
builder.Services.AddSingleton<NamedPipeCommandServer>();
builder.Services.AddHostedService<IranDirectWorker>();

IHost host = builder.Build();

await host.RunAsync();