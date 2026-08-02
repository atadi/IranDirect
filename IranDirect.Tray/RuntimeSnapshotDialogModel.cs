using IranDirect.Core.CustomRoutes;
using IranDirect.Core.Observability;
using IranDirect.Core.Runtime.Execution;
using IranDirect.Core.Runtime.Profiling;
using IranDirect.Core.Vpn;

namespace IranDirect.Tray;

public sealed record SnapshotSectionRow(
    string Label,
    string Value);

public sealed record SnapshotSection(
    string Title,
    IReadOnlyList<SnapshotSectionRow> Rows);

public static class RuntimeSnapshotDialogModel
{
    public const string NotAvailable = "-";

    public static IReadOnlyList<SnapshotSection> MapSections(
        RuntimeSnapshot snapshot)
    {
        return
        [
            new SnapshotSection(
                "Configuration",
                [
                    new SnapshotSectionRow(
                        "Desired",
                        YesNo(snapshot.Configuration?.Enabled)),
                    new SnapshotSectionRow(
                        "Repair interval",
                        Format(snapshot.Configuration?.RepairInterval)),
                    new SnapshotSectionRow(
                        "Prefix update interval",
                        Format(snapshot.Configuration?.PrefixUpdateInterval)),
                    new SnapshotSectionRow(
                        "VPN profile",
                        snapshot.Configuration?.VpnProfilePath
                        ?? NotAvailable),
                    new SnapshotSectionRow(
                        "Auto repair",
                        YesNo(snapshot.Configuration?.AutoRepair))
                ]),
            new SnapshotSection(
                "Runtime",
                [
                    new SnapshotSectionRow(
                        "Applied",
                        YesNo(snapshot.Runtime?.Enabled))
                ]),
            new SnapshotSection(
                "Operation",
                [
                    new SnapshotSectionRow(
                        "State",
                        FormatOperation(snapshot.Operation))
                ]),
            new SnapshotSection(
                "Routes",
                [
                    new SnapshotSectionRow(
                        "Managed",
                        FormatCount(snapshot.InstalledRouteCount)),
                    new SnapshotSectionRow(
                        "Desired",
                        FormatCount(snapshot.PrefixCount)),
                    new SnapshotSectionRow(
                        "Inventory",
                        FormatCount(snapshot.RouteInventoryCount))
                ]),
            new SnapshotSection(
                "VPN",
                [
                    new SnapshotSectionRow(
                        "Protected",
                        FormatProtected(snapshot.VpnEndpointHealth))
                ]),
            new SnapshotSection(
                "DNS",
                [
                    new SnapshotSectionRow(
                        "Domains",
                        snapshot.DnsCache.Count.ToString()),
                    new SnapshotSectionRow(
                        "Fresh",
                        CountState(
                            snapshot,
                            CustomRouteDnsCacheState.Fresh).ToString()),
                    new SnapshotSectionRow(
                        "Stale",
                        CountState(
                            snapshot,
                            CustomRouteDnsCacheState.Stale).ToString()),
                    new SnapshotSectionRow(
                        "Failed",
                        CountState(
                            snapshot,
                            CustomRouteDnsCacheState.Failed).ToString())
                ]),
            new SnapshotSection(
                "Performance",
                [
                    new SnapshotSectionRow(
                        "Last cycle",
                        FormatPerformance(snapshot.Performance))
                ])
        ];
    }

    public static string FormatPerformance(
        RuntimeCyclePerfReport? report) =>
        report is null
            ? "none"
            : $"{report.TotalMs / 1000.0:F1} sec";

    private static int CountState(
        RuntimeSnapshot snapshot,
        CustomRouteDnsCacheState state) =>
        snapshot.DnsCache.Count(
            status => status.State == state);

    private static string FormatOperation(
        RuntimeOperationSnapshot? operation) =>
        operation?.State switch
        {
            null => "None",
            OperationState.Idle => "Idle",
            OperationState.Enabling => "Enabling",
            OperationState.Disabling => "Disabling",
            OperationState.Repairing => "Repairing",
            OperationState.Failed => "Failed",
            _ => operation!.State.ToString()
        };

    private static string FormatProtected(
        VpnEndpointProtectionHealth? health) =>
        health is null
            ? NotAvailable
            : $"{health.ProtectedEndpointCount} / " +
              $"{health.CurrentEndpointCount}";

    private static string FormatCount(
        int? count) =>
        count is null
            ? NotAvailable
            : count.Value.ToString();

    private static string YesNo(
        bool? value) =>
        value switch
        {
            null => "Unknown",
            true => "Yes",
            false => "No"
        };

    private static string Format(
        TimeSpan? value) =>
        value is null
            ? NotAvailable
            : value.Value.ToString();
}
