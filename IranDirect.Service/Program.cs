using IranDirect.Core;
using IranDirect.Core.Networking;
using IranDirect.Core.Prefixes;
using IranDirect.Core.Routing;
using IranDirect.Core.State;
using IranDirect.Core.SystemTools;
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
builder.Services.AddSingleton<GatewayDetector>();
builder.Services.AddSingleton<CommandRunner>();

builder.Services.AddSingleton<
    IRouteManager,
    WindowsRouteManager>();

builder.Services.AddSingleton<RouteReconciler>();
builder.Services.AddSingleton<IranDirectController>();

builder.Services.AddSingleton<OperationCoordinator>();
builder.Services.AddSingleton<NamedPipeCommandServer>();
builder.Services.AddHostedService<IranDirectWorker>();

IHost host = builder.Build();

await host.RunAsync();