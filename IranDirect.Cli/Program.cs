using IranDirect.Core;
using IranDirect.Core.Ipc;

string commandText = args.Length == 0
    ? "status"
    : args[0].ToLowerInvariant();

if (!TryParseCommand(
        commandText,
        out IranDirectCommand command))
{
    Console.Error.WriteLine(
        "Usage: IranDirect.Cli " +
        "[update|enable|disable|repair|status|vpn-endpoints]");

    return 6;
}

IranDirectServiceClient client = new();

try
{
    ServiceResponse response =
        await client.SendAsync(command);

    if (!response.Success)
    {
        string prefix =
            string.IsNullOrWhiteSpace(response.ErrorCode)
                ? ""
                : $"[{response.ErrorCode}] ";

        Console.Error.WriteLine(
            prefix + response.Message);

        return 1;
    }

    if (command == IranDirectCommand.Status &&
        response.Status is not null)
    {
        WriteStatus(response.Status);
    }
    else if (
        command == IranDirectCommand.VpnEndpoints)
    {
        WriteVpnEndpoints(response);
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

static bool TryParseCommand(
    string value,
    out IranDirectCommand command)
{
    command = value switch
    {
        "status" => IranDirectCommand.Status,
        "update" => IranDirectCommand.UpdatePrefixes,
        "enable" => IranDirectCommand.Enable,
        "disable" => IranDirectCommand.Disable,
        "repair" => IranDirectCommand.Repair,
        "vpn-endpoints" => IranDirectCommand.VpnEndpoints,
        _ => default
    };

    return value is
        "status" or
        "update" or
        "enable" or
        "disable" or
        "repair" or
        "vpn-endpoints";
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

static void WriteVpnEndpoints(
    ServiceResponse response)
{
    Console.WriteLine(response.Message);

    foreach (var endpoint in response.VpnEndpoints)
    {
        Console.WriteLine(
            $"{endpoint.Address}:{endpoint.Port} " +
            $"({endpoint.Protocol}) " +
            $"from {endpoint.Host}");
    }
}