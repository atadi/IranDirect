using System.Text.Json;
using PathVeer.Core.CustomRoutes;
using PathVeer.Core.Observability;
using PathVeer.Core.Prefixes;
using PathVeer.Core.Routing;
using PathVeer.Core.Runtime;
using PathVeer.Core.Runtime.Execution;
using PathVeer.Core.Runtime.Profiling;
using PathVeer.Core.Vpn;

namespace PathVeer.Testing.Performance.Lifecycle;

/// <summary>
/// Thrown when a simulation verification invariant fails.
/// </summary>
public sealed class ServiceSimulationVerificationException : Exception
{
    public ServiceSimulationVerificationException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// Verifies the documented stability invariants of a lifecycle
/// simulation against the wired environment. Methods that need to
/// read persisted state are async; the rest are synchronous.
/// </summary>
public static class ServiceSimulationVerifier
{
    public static void AssertNoActiveExecution(
        SimulatedRuntimeEnvironment environment)
    {
        int active = environment.ExecutionHandler.ActiveCalls;

        if (active != 0)
        {
            throw new ServiceSimulationVerificationException(
                $"Execution handler still has {active} active calls.");
        }
    }

    public static void AssertOperationCompleted(
        SimulatedRuntimeEnvironment environment)
    {
        RuntimeOperationSnapshot snapshot =
            environment.OperationStatus.CreateSnapshot();

        if (!snapshot.IsCompleted)
        {
            throw new ServiceSimulationVerificationException(
                $"Operation snapshot is not completed: " +
                $"State={snapshot.State}, CompletedAt set=" +
                $"{snapshot.CompletedAt is not null}.");
        }
    }

    public static void AssertNoPendingTempFiles(
        SimulatedRuntimeEnvironment environment)
    {
        string[] pending = Directory.Exists(
            environment.Workspace.PathValue)
            ? Directory.EnumerateFiles(
                    environment.Workspace.PathValue,
                    "*.tmp",
                    SearchOption.AllDirectories)
                .ToArray()
            : [];

        if (pending.Length > 0)
        {
            throw new ServiceSimulationVerificationException(
                $"Workspace contains {pending.Length} pending " +
                $"temporary file(s): {string.Join(", ", pending)}.");
        }
    }

    public static void AssertJsonFileValid(string path)
    {
        if (!File.Exists(path))
        {
            throw new ServiceSimulationVerificationException(
                $"Expected JSON file does not exist: {path}");
        }

        try
        {
            using FileStream stream = File.OpenRead(path);
            using JsonDocument document = JsonDocument.Parse(stream);
        }
        catch (Exception ex)
        {
            throw new ServiceSimulationVerificationException(
                $"JSON file is not valid: {path}. {ex.Message}");
        }
    }

    public static void AssertPerfReportsValid(
        SimulatedRuntimeEnvironment environment)
    {
        RuntimePerfReportStore? store = environment.PerfReportStore;

        if (store is null)
        {
            return;
        }

        if (!Directory.Exists(store.Directory))
        {
            return;
        }

        string[] files = Directory.EnumerateFiles(
                store.Directory,
                RuntimePerfReportStore.FilePattern)
            .ToArray();

        foreach (string file in files)
        {
            AssertJsonFileValid(file);

            string json = File.ReadAllText(file);

            using JsonDocument document = JsonDocument.Parse(json);

            if (!TryGetTrigger(document.RootElement, out string? value))
            {
                throw new ServiceSimulationVerificationException(
                    $"Perf report {file} has no valid trigger.");
            }

            if (value is not ("enable" or "disable" or "repair"))
            {
                throw new ServiceSimulationVerificationException(
                    $"Perf report {file} has unexpected trigger " +
                    $"'{value}'.");
            }
        }
    }

    /// <summary>
    /// Reads the perf-report trigger property. <see
    /// cref="RuntimePerfReportStore"/> serializes with default
    /// (PascalCase) naming and reads back with
    /// <c>PropertyNameCaseInsensitive</c>, so on-disk reports carry
    /// <c>"Trigger"</c>. Both casings are accepted here to match the
    /// store's own case-insensitive contract.
    /// </summary>
    private static bool TryGetTrigger(
        JsonElement root,
        out string? trigger)
    {
        foreach (JsonProperty property in root.EnumerateObject())
        {
            if (!string.Equals(
                    property.Name,
                    "trigger",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (property.Value.ValueKind == JsonValueKind.String
                && property.Value.GetString()
                    is { Length: > 0 } value)
            {
                trigger = value;
                return true;
            }

            break;
        }

        trigger = null;
        return false;
    }

    public static async Task AssertConvergedToDesiredAsync(
        SimulatedRuntimeEnvironment environment,
        CancellationToken cancellationToken = default)
    {
        ObservedDirectGateway? gateway = environment.Observation.DirectGateway;

        if (gateway is null)
        {
            throw new ServiceSimulationVerificationException(
                "Observation state has no direct gateway.");
        }

        IReadOnlyList<SystemRoute> table =
            environment.RouteTable.Snapshot();

        HashSet<string> actualPrefixIdentities = table
            .Where(route => route.RouteMetric == 5)
            .Select(ToIdentity)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        HashSet<string> expectedPrefixIdentities = environment
            .Observation
            .Prefixes
            .Select(prefix => $"{prefix}|{gateway.Address}|{gateway.InterfaceIndex}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!expectedPrefixIdentities.SetEquals(
                actualPrefixIdentities))
        {
            throw new ServiceSimulationVerificationException(
                "Desired prefix routes are not installed exactly: " +
                $"expected {expectedPrefixIdentities.Count}, " +
                $"actual {actualPrefixIdentities.Count}.");
        }

        foreach (ObservedVpnEndpoint endpoint in
                 environment.Observation.VpnEndpoints)
        {
            string identity =
                $"{endpoint.Address}/32|{gateway.Address}|" +
                $"{gateway.InterfaceIndex}";

            if (!table.Any(route =>
                    ToIdentity(route).Equals(
                        identity,
                        StringComparison.OrdinalIgnoreCase)))
            {
                throw new ServiceSimulationVerificationException(
                    $"VPN endpoint route '{identity}' is missing.");
            }
        }

        await AssertInventoryMatchesRoutesAsync(
            environment,
            cancellationToken);
    }

    public static async Task AssertInventoryMatchesRoutesAsync(
        SimulatedRuntimeEnvironment environment,
        CancellationToken cancellationToken = default)
    {
        RouteInventory inventory =
            await environment.RouteInventoryStore.LoadAsync(
                cancellationToken);

        HashSet<string> actualIdentities = environment
            .RouteTable
            .Snapshot()
            .Where(route => route.RouteMetric == 5)
            .Select(ToIdentity)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (inventory.Routes.Count != actualIdentities.Count)
        {
            throw new ServiceSimulationVerificationException(
                "Route inventory does not match owned routes: " +
                $"inventory {inventory.Routes.Count}, routes " +
                $"{actualIdentities.Count}.");
        }

        foreach (RouteInventoryItem item in inventory.Routes)
        {
            if (!actualIdentities.Contains(item.Identity))
            {
                throw new ServiceSimulationVerificationException(
                    $"Inventory route '{item.Identity}' is not " +
                    "installed on the platform.");
            }
        }
    }

    public static async Task AssertDisableStateAsync(
        SimulatedRuntimeEnvironment environment,
        CancellationToken cancellationToken = default)
    {
        RouteInventory inventory =
            await environment.RouteInventoryStore.LoadAsync(
                cancellationToken);

        if (inventory.Routes.Count != 0)
        {
            throw new ServiceSimulationVerificationException(
                "Route inventory is not empty after disable: " +
                $"{inventory.Routes.Count} route(s).");
        }

        int prefixRouteCount = environment
            .RouteTable
            .Snapshot()
            .Count(route => route.RouteMetric == 5);

        if (prefixRouteCount != 0)
        {
            throw new ServiceSimulationVerificationException(
                $"Disable left {prefixRouteCount} managed prefix " +
                "route(s) on the platform.");
        }
    }

    public static async Task AssertDnsCacheRecordCountAsync(
        SimulatedRuntimeEnvironment environment,
        string domain,
        int maxRecords,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<CustomRouteDnsCacheStatus> statuses =
            await environment.DnsCacheService.GetStatusAsync(
                cancellationToken);

        int records = statuses.Count(status =>
            status.Domain.Equals(
                domain,
                StringComparison.OrdinalIgnoreCase));

        if (records > maxRecords)
        {
            throw new ServiceSimulationVerificationException(
                $"DNS cache has {records} record(s) for '{domain}', " +
                $"expected at most {maxRecords}.");
        }
    }

    public static void AssertMonitorNotOverlapping(
        SimulatedRuntimeEnvironment environment)
    {
        int peak = environment.CountingPrefixUpdateChecker
            .PeakActiveChecks;

        if (peak > 1)
        {
            throw new ServiceSimulationVerificationException(
                $"Prefix update checks overlapped: peak " +
                $"{peak} concurrent checks.");
        }
    }

    public static void AssertMonitorStopped(
        SimulatedRuntimeEnvironment environment)
    {
        PrefixUpdateMonitorSnapshot snapshot =
            environment.Monitor.GetSnapshot();

        if (snapshot.Running || snapshot.Checking)
        {
            throw new ServiceSimulationVerificationException(
                "Monitor is still running or checking after stop: " +
                $"Running={snapshot.Running}, Checking=" +
                $"{snapshot.Checking}.");
        }
    }

    public static void AssertSnapshotIsolated(
        RuntimeSnapshot first,
        RuntimeSnapshot second)
    {
        if (ReferenceEquals(first, second))
        {
            throw new ServiceSimulationVerificationException(
                "Two snapshot calls returned the same instance.");
        }

        if (ReferenceEquals(
                first.Runtime,
                second.Runtime))
        {
            throw new ServiceSimulationVerificationException(
                "Snapshots share a runtime status instance.");
        }

        if (ReferenceEquals(
                first.Configuration,
                second.Configuration))
        {
            throw new ServiceSimulationVerificationException(
                "Snapshots share a configuration instance.");
        }

        if (first.CapturedAt > second.CapturedAt)
        {
            throw new ServiceSimulationVerificationException(
                "Snapshot timestamps are not monotonic.");
        }
    }

    private static string ToIdentity(SystemRoute route) =>
        $"{route.DestinationPrefix}|{route.NextHop}|" +
        $"{route.InterfaceIndex}";
}
