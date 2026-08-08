using System.Diagnostics;
using System.Net;
using PathVeer.Core.CustomRoutes;
using PathVeer.Core.Prefixes;
using PathVeer.Core.Routing;
using PathVeer.Core.Vpn;

namespace PathVeer.Testing.Performance.Lifecycle;

/// <summary>
/// An informational resource sample captured at a point in a
/// simulation. Values are informational (see the harness README
/// section): assertions rely on the broad invariant checks in the
/// runner and verifier, not on these measurements.
/// </summary>
public sealed record ResourceSample
{
    public int CycleIndex { get; init; }

    public long ManagedMemoryBytes { get; init; }

    public int GcGen0 { get; init; }

    public int GcGen1 { get; init; }

    public int GcGen2 { get; init; }

    public int ThreadCount { get; init; }

    public int ActiveExecutionHandlerCalls { get; init; }

    public long TotalExecutionHandlerCalls { get; init; }

    public int PeakExecutionHandlerCalls { get; init; }

    public int RouteTableEntries { get; init; }

    public int RouteInventoryCount { get; init; }

    public int VpnEndpointInventoryCount { get; init; }

    public int CustomRouteCount { get; init; }

    public int DnsCacheRecordCount { get; init; }

    public int PrefixCount { get; init; }

    public int PrefixHistoryEntryCount { get; init; }

    public int PerfReportFileCount { get; init; }

    public int PendingTempFileCount { get; init; }

    public int RouteAddCalls { get; init; }

    public int RouteDeleteCalls { get; init; }

    public int DnsResolutionCount { get; init; }

    public bool MonitorRunning { get; init; }

    public bool MonitorChecking { get; init; }

    public int MonitorConsecutiveFailures { get; init; }

    public static async Task<ResourceSample> CaptureAsync(
        SimulatedRuntimeEnvironment environment,
        int cycleIndex,
        CancellationToken cancellationToken = default)
    {
        RouteInventory routeInventory =
            await environment.RouteInventoryStore.LoadAsync(
                cancellationToken);

        VpnEndpointInventory endpointInventory =
            await environment.VpnEndpointInventoryStore.LoadAsync(
                cancellationToken);

        IReadOnlyList<string> prefixes =
            await environment.PrefixStore.LoadPrefixesAsync(
                environment.Country,
                cancellationToken);

        IReadOnlyList<CustomRouteEntry> customRoutes =
            await environment.CustomRouteService.GetAllAsync(
                cancellationToken);

        IReadOnlyList<CustomRouteDnsCacheStatus> dnsCache =
            await environment.DnsCacheService.GetStatusAsync(
                cancellationToken);

        IReadOnlyList<PrefixSourceUpdateHistoryEntry> history =
            await environment.PrefixHistoryService.GetRecentAsync(
                environment.Country,
                cancellationToken: cancellationToken);

        Process process = Process.GetCurrentProcess();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        PrefixUpdateMonitorSnapshot monitor = environment.Monitor.GetSnapshot();

        return new ResourceSample
        {
            CycleIndex = cycleIndex,
            ManagedMemoryBytes = GC.GetTotalMemory(forceFullCollection: false),
            GcGen0 = GC.CollectionCount(0),
            GcGen1 = GC.CollectionCount(1),
            GcGen2 = GC.CollectionCount(2),
            ThreadCount = process.Threads.Count,
            ActiveExecutionHandlerCalls =
                environment.ExecutionHandler.ActiveCalls,
            TotalExecutionHandlerCalls =
                environment.ExecutionHandler.TotalCalls,
            PeakExecutionHandlerCalls =
                environment.ExecutionHandler.PeakActiveCalls,
            RouteTableEntries =
                environment.RouteTable.Peek().Count,
            RouteInventoryCount = routeInventory.Routes.Count,
            VpnEndpointInventoryCount =
                endpointInventory.Endpoints.Count,
            CustomRouteCount = customRoutes.Count,
            DnsCacheRecordCount = dnsCache.Count,
            PrefixCount = prefixes.Count,
            PrefixHistoryEntryCount = history.Count,
            PerfReportFileCount =
                environment.PerfReportStore is null
                    ? 0
                    : SafeCountJson(
                        environment.PerfReportStore.Directory),
            PendingTempFileCount =
                CountTempFiles(environment.Workspace.PathValue),
            RouteAddCalls = environment.RouteTable.AddCalls,
            RouteDeleteCalls = environment.RouteTable.DeleteCalls,
            DnsResolutionCount =
                environment.DnsResolver.TotalResolutionCount,
            MonitorRunning = monitor.Running,
            MonitorChecking = monitor.Checking,
            MonitorConsecutiveFailures =
                monitor.ConsecutiveFailures
        };
    }

    private static int CountTempFiles(string directory) =>
        SafeCount(directory, "*.tmp");

    private static int SafeCountJson(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return 0;
        }

        try
        {
            return Directory.EnumerateFiles(
                    directory,
                    "*.json")
                .Count();
        }
        catch
        {
            return 0;
        }
    }

    private static int SafeCount(string directory, string pattern)
    {
        if (!Directory.Exists(directory))
        {
            return 0;
        }

        try
        {
            return Directory.EnumerateFiles(
                    directory,
                    pattern,
                    SearchOption.AllDirectories)
                .Count();
        }
        catch
        {
            return 0;
        }
    }
}
