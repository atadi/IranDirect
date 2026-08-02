using IranDirect.Core;
using IranDirect.Core.Cli;
using IranDirect.Core.Ipc;
using IranDirect.Core.Runtime.Execution;
using IranDirect.Core.Runtime.Profiling;
using IranDirect.Cli;
using System.Linq;

string commandText = args.Length == 0
    ? "status"
    : args[0].ToLowerInvariant();

    if (commandText == "profile")
    {
        string subcommand =
            args.ElementAtOrDefault(1)?.ToLowerInvariant()
            ?? "last";

        return subcommand switch
        {
            "last" => await ShowLatestProfileAsync(null),
            "enable" => await ShowLatestProfileAsync("enable"),
            "disable" => await ShowLatestProfileAsync("disable"),
            "repair" => await ShowLatestProfileAsync("repair"),
            "list" => await ShowProfileListAsync(
                args.ElementAtOrDefault(2)),
            _ => ShowProfileUsage()
        };
    }

    if (commandText == "custom-routes")
    {
        return await CustomRouteCliRunner.RunAsync(
            args.Skip(1).ToArray(),
            new IranDirectServiceClient());
    }

    if (commandText == "prefix-update")
    {
        return await PrefixUpdateCliRunner.RunAsync(
            args.Skip(1).ToArray(),
            new IranDirectServiceClient());
    }

    if (commandText == "doctor")
    {
        return await DiagnosticCliRunner.RunAsync(
            args.Skip(1).ToArray(),
            new IranDirectServiceClient());
    }

    if (commandText == "snapshot")
    {
        return await ShowSnapshotAsync();
    }

if (!TryParseCommand(
        commandText,
        out IranDirectCommand command))
{
    Console.Error.WriteLine(
        "Usage: IranDirect.Cli " +
        "[update|enable|disable|repair|status|vpn-endpoints|diagnostics|config|get-config|set-enabled|set-profile|runtime-plan|snapshot|prefix-update|custom-routes|doctor|profile]");

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
    else if (
        command == IranDirectCommand.RuntimePlan &&
        response.RuntimePlan is not null)
    {
        WriteRuntimePlan(response.RuntimePlan);
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
        "runtime-plan" => IranDirectCommand.RuntimePlan,
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
        "set-profile" or
        "runtime-plan";
}

static void WriteStatus(
    IranDirectStatus status)
{
    Console.WriteLine("=== Iran Direct status ===");

    string desired = status.DesiredEnabled.HasValue
        ? (status.DesiredEnabled.Value ? "Enabled" : "Disabled")
        : "Unknown";
    string applied = status.Enabled ? "Enabled" : "Disabled";
    Console.WriteLine($"Desired: {desired}");
    Console.WriteLine($"Applied: {applied}");

    if (status.Operation is not null && status.Operation.State != OperationState.Idle)
    {
        if (status.Operation.State == OperationState.Failed)
        {
            Console.WriteLine("Operation: Failed");

            int completed = status.Operation.CompletedSteps;
            int planned = status.Operation.PlannedSteps;
            if (planned > 0)
                Console.WriteLine($"Progress: {completed} / {planned}");

            if (!string.IsNullOrWhiteSpace(status.Operation.ErrorMessage))
                Console.WriteLine($"Operation error: {status.Operation.ErrorMessage}");
        }
        else
        {
            string opState = status.Operation.State switch
            {
                OperationState.Enabling => "Enabling",
                OperationState.Disabling => "Disabling",
                OperationState.Repairing => "Repairing",
                _ => status.Operation.State.ToString()
            };
            string suffix = status.Operation.IsCompleted ? " (Completed)" : "";
            Console.WriteLine($"Operation: {opState}{suffix}");

            int completed = status.Operation.CompletedSteps;
            int planned = status.Operation.PlannedSteps;
            if (planned > 0)
                Console.WriteLine($"Progress: {completed} / {planned}");

            if (status.Operation.CompletedAt is not null)
                Console.WriteLine($"Completed: {status.Operation.CompletedAt:yyyy-MM-dd HH:mm:ss UTC}");

            if (!string.IsNullOrWhiteSpace(status.Operation.ErrorMessage))
                Console.WriteLine($"Operation error: {status.Operation.ErrorMessage}");
        }
    }

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
static void WriteRuntimePlan(
    IranDirect.Core.Runtime.RuntimePlanSnapshot snapshot)
{
    Console.WriteLine("=== Runtime Plan ===");
    Console.WriteLine(
        $"Desired enabled: {snapshot.Desired.Enabled}");
    Console.WriteLine(
        $"Can reconcile: {snapshot.Desired.CanReconcile}");
    Console.WriteLine(
        $"Endpoint routes: " +
        $"{snapshot.Desired.EndpointRoutes.Count}");
    Console.WriteLine(
        $"Prefix routes: " +
        $"{snapshot.Desired.PrefixRoutes.Count}");
    Console.WriteLine(
        $"Observed routes: " +
        $"{snapshot.Observed.Routes.Count}");
    Console.WriteLine(
        $"Observed at: {snapshot.Observed.ObservedAt}");
    Console.WriteLine(
        $"Planned at: {snapshot.PlannedAt}");

    if (snapshot.Desired.Blockers.Count == 0)
    {
        Console.WriteLine("Blockers: none");
        return;
    }

    Console.WriteLine("Blockers:");

    foreach (var blocker in snapshot.Desired.Blockers)
    {
        Console.WriteLine(
            $"- [{blocker.Code}] {blocker.Message}");
    }
}

static async Task<int> ShowLatestProfileAsync(
    string? trigger)
{
    RuntimePerfReportStore store = new(
        RuntimePerfReportStore.DefaultDirectory);

    RuntimeCyclePerfReport? report;

    try
    {
        report = trigger is null
            ? await store.ReadLatestAsync()
            : await store.ReadLatestAsync(trigger);
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine(
            $"Failed to read profile report: {exception.Message}");

        return 1;
    }

    if (report is null)
    {
        Console.WriteLine(
            trigger is null
                ? "No profile reports found."
                : $"No '{trigger}' profile reports found.");
        Console.WriteLine(
            "Reports are written to " +
            RuntimePerfReportStore.DefaultDirectory +
            " after each enable/disable/repair cycle.");

        return 0;
    }

    WriteReport(report);

    return 0;
}

static async Task<int> ShowSnapshotAsync()
{
    IranDirectServiceClient client = new();

    try
    {
        ServiceResponse response =
            await client.SendAsync(
                IranDirectCommand.RuntimeSnapshot);

        if (!response.Success || response.Snapshot is null)
        {
            Console.Error.WriteLine(response.Message);
            return 1;
        }

        foreach (string line in
                 RuntimeSnapshotCliRenderer.Render(
                     response.Snapshot))
        {
            Console.WriteLine(line);
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
            $"IranDirect command failed: " +
            $"{exception.Message}");
        return 1;
    }
}

static void WriteReport(RuntimeCyclePerfReport report)
{
    Console.WriteLine("=== Latest profile report ===");
    Console.WriteLine($"Trigger: {report.Trigger}");
    Console.WriteLine($"Status: {report.CompletionStatus}");
    Console.WriteLine(
        $"Started: {report.StartedAt:yyyy-MM-dd HH:mm:ss} UTC");
    Console.WriteLine(
        $"Completed: {report.CompletedAt:yyyy-MM-dd HH:mm:ss} UTC");
    Console.WriteLine($"Total: {report.TotalMs:F1} ms");
    Console.WriteLine(
        $"Steps: {report.CompletedSteps}/{report.PlannedSteps}");

    if (!string.IsNullOrWhiteSpace(report.ErrorSummary))
    {
        Console.WriteLine(
            $"Error: {report.ErrorSummary.ReplaceLineEndings(" ")}");
    }

    Console.WriteLine();

    Console.WriteLine(
        $"{"Category",-32} " +
        $"{"Count",6} " +
        $"{"Total ms",10} " +
        $"{"Avg ms",8} " +
        $"{"Min ms",8} " +
        $"{"Max ms",8} " +
        $"{"P95 ms",8}");

    foreach (RuntimePerfCategorySummary category
        in report.Categories)
    {
        Console.WriteLine(
            $"{category.Category,-32} " +
            $"{category.Count,6} " +
            $"{category.TotalMs,10:F1} " +
            $"{category.AverageMs,8:F2} " +
            $"{category.MinMs,8:F1} " +
            $"{category.MaxMs,8:F1} " +
            $"{category.P95Ms,8:F1}");
    }
}

static async Task<int> ShowProfileListAsync(
    string? limitText)
{
    RuntimePerfReportStore store = new(
        RuntimePerfReportStore.DefaultDirectory);

    int limit = 10;

    if (!string.IsNullOrWhiteSpace(limitText)
        && !int.TryParse(limitText, out limit))
    {
        Console.Error.WriteLine(
            "Usage: IranDirect.Cli profile list [limit]");
        return 6;
    }

    try
    {
        IReadOnlyList<RuntimeCyclePerfReport> reports =
            await store.ListAsync(limit: limit);

        if (reports.Count == 0)
        {
            Console.WriteLine("No profile reports found.");
            return 0;
        }

        Console.WriteLine("=== Recent profile reports ===");
        Console.WriteLine(
            $"{"Trigger",-8} " +
            $"{"Status",-18} " +
            $"{"Completed",-20} " +
            $"{"Duration ms",-12} " +
            $"{"Steps",-8}");

        foreach (RuntimeCyclePerfReport report in reports)
        {
            Console.WriteLine(
                $"{report.Trigger,-8} " +
                $"{report.CompletionStatus,-18} " +
                $"{report.CompletedAt:yyyy-MM-dd HH:mm:ss} " +
                $"{report.TotalMs,12:F1} " +
                $"{report.CompletedSteps,5}/" +
                $"{report.PlannedSteps,2}");
        }
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine(
            $"Failed to list profile reports: {exception.Message}");
        return 1;
    }

    return 0;
}

static int ShowProfileUsage()
{
    Console.Error.WriteLine(
        "Usage: IranDirect.Cli profile " +
        "[last|enable|disable|repair|list [limit]]");
    return 6;
}