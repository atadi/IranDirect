using IranDirect.Core;
using IranDirect.Core.Networking;
using IranDirect.Core.Prefixes;
using IranDirect.Core.Routing;
using IranDirect.Core.State;
using IranDirect.Core.SystemTools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

string dataDirectory = Path.Combine(
    Environment.GetFolderPath(
        Environment.SpecialFolder.LocalApplicationData),
    "IranDirect");

Directory.CreateDirectory(dataDirectory);

HostApplicationBuilder builder =
    Host.CreateApplicationBuilder(args);

builder.Logging.SetMinimumLevel(
    LogLevel.Warning);

builder.Services.AddHttpClient<IranPrefixProvider>(
    client =>
    {
        client.Timeout =
            TimeSpan.FromSeconds(30);

        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "IranDirect/0.1");
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

builder.Services.AddSingleton<GatewayDetector>();
builder.Services.AddSingleton<CommandRunner>();

builder.Services.AddSingleton<
    IRouteManager,
    WindowsRouteManager>();

builder.Services.AddSingleton<RouteReconciler>();
builder.Services.AddSingleton<IranDirectController>();

using IHost host = builder.Build();

IranDirectController controller =
    host.Services.GetRequiredService<
        IranDirectController>();

string command = args.Length == 0
    ? "status"
    : args[0].ToLowerInvariant();

try
{
    switch (command)
    {
        case "update":
            {
                Console.WriteLine(
                    "Downloading Iranian IPv4 prefixes...");

                int count =
                    await controller.UpdatePrefixesAsync();

                Console.WriteLine(
                    $"Saved {count} prefixes.");

                break;
            }

        case "enable":
            {
                EnsureAdministrator();

                Console.WriteLine(
                    "Enabling Iran Direct...");

                ReconciliationResult result =
                    await controller.EnableAsync();

                Console.WriteLine(
                    $"Desired routes: {result.DesiredCount}");

                Console.WriteLine(
                    $"Already present: {result.ExistingCount}");

                Console.WriteLine(
                    $"Added: {result.AddedCount}");

                break;
            }

        case "disable":
            {
                EnsureAdministrator();

                Console.WriteLine(
                    "Disabling Iran Direct...");

                ReconciliationResult result =
                    await controller.DisableAsync();

                Console.WriteLine(
                    $"Removed: {result.RemovedCount}");

                break;
            }

        case "repair":
            {
                EnsureAdministrator();

                await controller.RepairAsync();

                Console.WriteLine(
                    "Repair completed.");

                break;
            }

        case "status":
            {
                IranDirectStatus status =
                    await controller.GetStatusAsync();

                Console.WriteLine(
                    "=== Iran Direct status ===");

                Console.WriteLine(
                    $"Enabled: {status.Enabled}");

                Console.WriteLine(
                    $"Gateway: {status.Gateway ?? "Unknown"}");

                Console.WriteLine(
                    $"Interface: " +
                    $"{status.InterfaceName ?? "Unknown"} " +
                    $"({status.InterfaceIndex})");

                Console.WriteLine(
                    $"Routes: " +
                    $"{status.InstalledRouteCount}/" +
                    $"{status.PrefixCount}");

                Console.WriteLine(
                    $"Prefixes updated: " +
                    $"{status.PrefixesUpdatedAt}");

                if (!string.IsNullOrWhiteSpace(
                        status.LastError))
                {
                    Console.WriteLine(
                        $"Last error: {status.LastError}");
                }

                break;
            }

        default:
            Console.Error.WriteLine(
                "Usage: IranDirect.Cli " +
                "[update|enable|disable|repair|status]");

            Environment.ExitCode = 6;
            break;
    }
}
catch (UnauthorizedAccessException exception)
{
    Console.Error.WriteLine(exception.Message);
    Environment.ExitCode = 2;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception);
    Environment.ExitCode = 1;
}

static void EnsureAdministrator()
{
    using System.Security.Principal.WindowsIdentity identity =
        System.Security.Principal.WindowsIdentity
            .GetCurrent();

    System.Security.Principal.WindowsPrincipal principal =
        new(identity);

    bool administrator =
        principal.IsInRole(
            System.Security.Principal
                .WindowsBuiltInRole.Administrator);

    if (!administrator)
    {
        throw new UnauthorizedAccessException(
            "This command must be run from an " +
            "Administrator terminal.");
    }
}