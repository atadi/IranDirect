using PathVeer.Core.CustomRoutes;
using PathVeer.Core.Observability;
using PathVeer.Core.Prefixes;
using PathVeer.Core.Runtime.Execution;
using PathVeer.Core.Runtime.Profiling;
using PathVeer.Core.Vpn;

namespace PathVeer.Tray;

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
                ]),
            new SnapshotSection(
                "Prefix Source",
                BuildPrefixSourceRows(
                    snapshot.PrefixSource)),
            new SnapshotSection(
                "Prefix Update",
                BuildPrefixUpdateRows(
                    snapshot.PrefixUpdate,
                    snapshot.PrefixUpdateMonitor))
        ];
    }

    private static IReadOnlyList<SnapshotSectionRow>
        BuildPrefixSourceRows(
            PrefixSourceMetadata? source)
    {
        if (source is null)
        {
            return
            [
                new SnapshotSectionRow("Source", NotAvailable),
                new SnapshotSectionRow("Status", NotAvailable),
                new SnapshotSectionRow("Last Success", NotAvailable),
                new SnapshotSectionRow("Prefix Count", NotAvailable),
                new SnapshotSectionRow("Added", NotAvailable),
                new SnapshotSectionRow("Removed", NotAvailable),
                new SnapshotSectionRow("Unchanged", NotAvailable),
                new SnapshotSectionRow("Hash", NotAvailable)
            ];
        }

        return
        [
            new SnapshotSectionRow(
                "Source",
                FormatSourceName(source)),
            new SnapshotSectionRow(
                "Status",
                FormatStatus(source.LastStatus)),
            new SnapshotSectionRow(
                "Last Success",
                FormatTimestamp(source.LastSucceededAt)),
            new SnapshotSectionRow(
                "Prefix Count",
                FormatCount(source.PrefixCount)),
            new SnapshotSectionRow(
                "Added",
                FormatCount(
                    source.ChangeSummary?.AddedCount)),
            new SnapshotSectionRow(
                "Removed",
                FormatCount(
                    source.ChangeSummary?.RemovedCount)),
            new SnapshotSectionRow(
                "Unchanged",
                FormatCount(
                    source.ChangeSummary?.UnchangedCount)),
            new SnapshotSectionRow(
                "Hash",
                ShortenHash(source.ContentHash))
        ];
    }

    private static IReadOnlyList<SnapshotSectionRow>
        BuildPrefixUpdateRows(
            PrefixUpdateCheckResult? update,
            PrefixUpdateMonitorSnapshot? monitor)
    {
        List<SnapshotSectionRow> rows =
        [
            new SnapshotSectionRow(
                "Monitor Running",
                FormatMonitorBool(monitor?.Running)),
            new SnapshotSectionRow(
                "Checking",
                FormatMonitorBool(monitor?.Checking)),
            new SnapshotSectionRow(
                "Last Checked",
                FormatTimestamp(monitor?.LastCheckedAt)),
            new SnapshotSectionRow(
                "Last Successful Check",
                FormatTimestamp(
                    monitor?.LastSuccessfulCheckAt)),
            new SnapshotSectionRow(
                "Consecutive Failures",
                monitor?.ConsecutiveFailures.ToString()
                ?? NotAvailable)
        ];

        if (update is null)
        {
            rows.Add(new SnapshotSectionRow(
                "Status", NotAvailable));
            rows.Add(new SnapshotSectionRow(
                "Remote Last Modified", NotAvailable));
            rows.Add(new SnapshotSectionRow(
                "Remote ETag", NotAvailable));
            rows.Add(new SnapshotSectionRow(
                "Remote Size", NotAvailable));
            rows.Add(new SnapshotSectionRow(
                "Reason", NotAvailable));

            return rows;
        }

        rows.Add(new SnapshotSectionRow(
            "Status",
            FormatUpdateStatus(update.Status)));
        rows.Add(new SnapshotSectionRow(
            "Remote Last Modified",
            FormatTimestamp(
                update.RemoteMetadata?.LastModified)));
        rows.Add(new SnapshotSectionRow(
            "Remote ETag",
            update.RemoteMetadata?.ETag ?? NotAvailable));
        rows.Add(new SnapshotSectionRow(
            "Remote Size",
            FormatBytes(
                update.RemoteMetadata?.ContentLength)));
        rows.Add(new SnapshotSectionRow(
            "Reason",
            update.Reason ?? NotAvailable));

        return rows;
    }

    private static string FormatMonitorBool(
        bool? value) =>
        value is null
            ? NotAvailable
            : value.Value ? "Yes" : "No";

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

    private static string FormatSourceName(
        PrefixSourceMetadata source) =>
        string.IsNullOrWhiteSpace(source.SourceDisplayName)
            ? source.SourceId
            : source.SourceDisplayName;

    private static string FormatStatus(
        PrefixSourceUpdateStatus status) =>
        status switch
        {
            PrefixSourceUpdateStatus.NeverUpdated =>
                "Never updated",
            PrefixSourceUpdateStatus.Succeeded =>
                "Succeeded",
            PrefixSourceUpdateStatus.NotModified =>
                "Not modified",
            PrefixSourceUpdateStatus.Failed =>
                "Failed",
            _ => status.ToString()
        };

    private static string FormatUpdateStatus(
        PrefixUpdateCheckStatus status) =>
        status switch
        {
            PrefixUpdateCheckStatus.Current => "Current",
            PrefixUpdateCheckStatus.UpdateAvailable =>
                "Update Available",
            PrefixUpdateCheckStatus.Unknown => "Unknown",
            PrefixUpdateCheckStatus.Failed => "Failed",
            _ => status.ToString()
        };

    private static string FormatBytes(
        long? contentLength) =>
        contentLength is null
            ? NotAvailable
            : contentLength.Value.ToString();

    private static string FormatTimestamp(
        DateTimeOffset? timestamp) =>
        timestamp is null
            ? NotAvailable
            : timestamp.Value.ToLocalTime().ToString(
                "yyyy-MM-dd HH:mm:ss");

    private static string ShortenHash(
        string? hash)
    {
        if (string.IsNullOrEmpty(hash))
        {
            return NotAvailable;
        }

        return hash.Length <= 8
            ? hash
            : hash[..8] + "...";
    }

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
