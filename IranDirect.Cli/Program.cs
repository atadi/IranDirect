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
        "[update|enable|disable|repair|status|vpn-endpoints|diagnostics|config|get-config|set-enabled|set-profile]");

    return 6;
}

IranDirectServiceClient client = new();

try
{
    string? value =
        command is
            IranDirectCommand.SetConfigurationEnabled or
            IranDirectCommand.SetConfigurationProfilePath
            ? args.ElementAtOrDefault(1)
            : null;

    ServiceResponse response =
        await client.SendAsync(
            command,
            value);

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
    else if (
        command == IranDirectCommand.Diagnostics &&
        response.Diagnostics is not null)
    {
        WriteDiagnostics(response.Diagnostics);
    }
    else if (
        command is
            IranDirectCommand.GetConfiguration or
            IranDirectCommand.SetConfigurationEnabled or
            IranDirectCommand.SetConfigurationProfilePath &&
        response.Configuration is not null)
    {
        WriteConfiguration(response.Configuration);
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
        "diagnostics" => IranDirectCommand.Diagnostics,
        "config" => IranDirectCommand.GetConfiguration,
        "get-config" => IranDirectCommand.GetConfiguration,
        "set-enabled" => IranDirectCommand.SetConfigurationEnabled,
        "set-profile" => IranDirectCommand.SetConfigurationProfilePath,
        _ => default
    };

    return value is
        "status" or
        "update" or
        "enable" or
        "disable" or
        "repair" or
        "vpn-endpoints" or
        "diagnostics" or
        "config" or
        "get-config" or
        "set-enabled" or
        "set-profile";
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
    Console.WriteLine(
        $"VPN endpoints: " +
        $"{status.ProtectedVpnEndpointCount}/" +
        $"{status.VpnEndpointCount} protected");
    Console.WriteLine(
        $"VPN protection healthy: " +
        $"{status.VpnEndpointsProtected}");

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
static void WriteDiagnostics(
    IranDirect.Core.Diagnostics.IranDirectDiagnostics diagnostics)
{
    Console.WriteLine("=== IranDirect Diagnostics ===");
    Console.WriteLine($"Version: {diagnostics.Version}");
    Console.WriteLine($"Generated: {diagnostics.GeneratedAt}");
    Console.WriteLine(
        $"Overall: {diagnostics.OverallSeverity}");
    Console.WriteLine();

    foreach (var check in diagnostics.Checks)
    {
        Console.WriteLine(
            $"[{check.Severity}] {check.Name}: " +
            $"{check.Message}");
    }
}
static void WriteConfiguration(
    IranDirect.Core.Configuration.DesiredConfiguration configuration)
{
    Console.WriteLine("=== Desired Configuration ===");
    Console.WriteLine(
        $"Schema version: {configuration.SchemaVersion}");
    Console.WriteLine($"Enabled: {configuration.Enabled}");
    Console.WriteLine(
        $"VPN provider: {configuration.VpnProvider}");
    Console.WriteLine(
        $"VPN profile path: {configuration.VpnProfilePath}");
    Console.WriteLine(
        $"Auto repair: {configuration.AutoRepair}");
    Console.WriteLine(
        $"Repair interval: {configuration.RepairInterval}");
    Console.WriteLine(
        $"Auto update prefixes: " +
        $"{configuration.AutoUpdatePrefixes}");
    Console.WriteLine(
        $"Prefix update interval: " +
        $"{configuration.PrefixUpdateInterval}");
}