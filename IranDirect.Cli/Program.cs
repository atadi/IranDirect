using IranDirect.Core;
using IranDirect.Core.Ipc;

string command = args.Length == 0
    ? "status"
    : args[0].ToLowerInvariant();

if (!IsSupportedCommand(command))
{
    Console.Error.WriteLine(
        "Usage: IranDirect.Cli " +
        "[update|enable|disable|repair|status]");

    return 6;
}

IranDirectServiceClient client = new();

try
{
    ServiceResponse response =
        await client.SendAsync(command);

    if (!response.Success)
    {
        Console.Error.WriteLine(response.Message);
        return 1;
    }

    if (command == "status" &&
        response.Status is not null)
    {
        WriteStatus(response.Status);
    }
    else
    {
        Console.WriteLine(response.Message);
    }

    return 0;
}
catch (TimeoutException exception)
{
    Console.Error.WriteLine(exception.Message);
    return 3;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine(
        "The operation was canceled.");

    return 4;
}
catch (Exception exception)
{
    Console.Error.WriteLine(
        $"IranDirect command failed: {exception.Message}");

    return 1;
}

static bool IsSupportedCommand(
    string command)
{
    return command is
        "update" or
        "enable" or
        "disable" or
        "repair" or
        "status";
}

static void WriteStatus(
    IranDirectStatus status)
{
    Console.WriteLine("=== Iran Direct status ===");
    Console.WriteLine($"Enabled: {status.Enabled}");
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
        $"Prefixes updated: {status.PrefixesUpdatedAt}");

    if (!string.IsNullOrWhiteSpace(
            status.LastError))
    {
        Console.WriteLine(
            $"Last error: {status.LastError}");
    }
}