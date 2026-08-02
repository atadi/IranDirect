using IranDirect.Core.CustomRoutes;
using IranDirect.Core.Observability;
using IranDirect.Core.Prefixes;
using IranDirect.Core.Runtime.Execution;
using IranDirect.Core.Runtime.Profiling;
using IranDirect.Core.Vpn;

namespace IranDirect.Core.Cli;

public static class RuntimeSnapshotCliRenderer
{
    public static IEnumerable<string> Render(
        RuntimeSnapshot snapshot)
    {
        yield return "=== Runtime Snapshot ===";
        yield return $"Captured: {Format(snapshot.CapturedAt)}";
        yield return "";

        yield return "Configuration:";
        yield return $"Desired: {YesNo(snapshot.Configuration?.Enabled)}";
        yield return "";

        yield return "Runtime:";
        yield return $"Applied: {YesNo(snapshot.Runtime?.Enabled)}";
        yield return "";

        yield return "Operation:";
        yield return $"{FormatOperation(snapshot.Operation)}";
        yield return "";

        yield return "Routes:";
        yield return $"Managed: {FormatCount(snapshot.InstalledRouteCount)}";
        yield return $"Desired: {FormatCount(snapshot.PrefixCount)}";
        yield return $"Inventory: {FormatCount(snapshot.RouteInventoryCount)}";
        yield return "";

        yield return "VPN:";
        yield return $"Protected: {FormatProtected(snapshot.VpnEndpointHealth)}";
        yield return "";

        yield return "DNS:";
        yield return $"Domains: {snapshot.DnsCache.Count}";
        yield return $"Fresh: {CountState(snapshot, CustomRouteDnsCacheState.Fresh)}";
        yield return $"Stale: {CountState(snapshot, CustomRouteDnsCacheState.Stale)}";
        yield return $"Failed: {CountState(snapshot, CustomRouteDnsCacheState.Failed)}";
        yield return "";

        yield return "Performance:";
        yield return $"Last cycle: {FormatPerformance(snapshot.Performance)}";
        yield return "";

        yield return "Prefix Source:";

        if (snapshot.PrefixSource is null)
        {
            yield return "Unavailable.";
            yield return "";
        }
        else
        {
            PrefixSourceMetadata source = snapshot.PrefixSource;

            yield return $"Source: {FormatSourceName(source)}";
            yield return $"Status: {FormatStatus(source.LastStatus)}";
            yield return $"Last success: {FormatTimestamp(source.LastSucceededAt)}";
            yield return $"Last attempted: {FormatTimestamp(source.LastAttemptedAt)}";
            yield return $"Prefixes: {source.PrefixCount}";
            yield return $"Hash: {ShortenHash(source.ContentHash)}";
            yield return $"Added: {FormatChange(source.ChangeSummary?.AddedCount)}";
            yield return $"Removed: {FormatChange(source.ChangeSummary?.RemovedCount)}";
            yield return $"Unchanged: {FormatChange(source.ChangeSummary?.UnchangedCount)}";
            yield return $"Changed: {FormatChanged(source.ChangeSummary)}";
            yield return "";
        }

        yield return "Prefix Update:";

        if (snapshot.PrefixUpdate is null)
        {
            yield return "Unavailable.";
            yield return "";
        }
        else
        {
            PrefixUpdateCheckResult update = snapshot.PrefixUpdate;
            PrefixUpdateCheckRemoteMetadata? remote =
                update.RemoteMetadata;

            yield return "Status:";
            yield return FormatUpdateStatus(update.Status);
            yield return "";

            if (remote?.LastModified is { } lastModified)
            {
                yield return "Remote Last Modified:";
                yield return Format(lastModified);
                yield return "";
            }

            if (remote?.ETag is { } etag)
            {
                yield return "Remote ETag:";
                yield return etag;
                yield return "";
            }

            if (remote?.ContentLength is { } contentLength)
            {
                yield return "Remote Content Length:";
                yield return contentLength.ToString();
                yield return "";
            }

            if (!string.IsNullOrWhiteSpace(update.Reason))
            {
                yield return "Reason:";
                yield return update.Reason;
                yield return "";
            }
        }

        yield return "Configuration:";

        if (snapshot.Configuration is null)
        {
            yield return "Unavailable.";
            yield break;
        }

        yield return $"Repair interval: {snapshot.Configuration.RepairInterval}";
        yield return $"Prefix update interval: {snapshot.Configuration.PrefixUpdateInterval}";
        yield return $"VPN profile: {snapshot.Configuration.VpnProfilePath}";
        yield return $"Auto repair: {snapshot.Configuration.AutoRepair}";
    }

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
            ? "Unavailable"
            : $"{health.ProtectedEndpointCount} / {health.CurrentEndpointCount}";

    private static string FormatPerformance(
        RuntimeCyclePerfReport? report) =>
        report is null
            ? "none"
            : $"{report.TotalMs / 1000.0:F1} sec";

    private static string FormatCount(
        int? count) =>
        count is null ? "-" : count.Value.ToString();

    private static string FormatSourceName(
        PrefixSourceMetadata source) =>
        string.IsNullOrWhiteSpace(source.SourceDisplayName)
            ? source.SourceId
            : source.SourceDisplayName;

    private static string FormatStatus(
        PrefixSourceUpdateStatus status) =>
        status switch
        {
            PrefixSourceUpdateStatus.NeverUpdated => "Never updated",
            PrefixSourceUpdateStatus.Succeeded => "Succeeded",
            PrefixSourceUpdateStatus.NotModified => "Not modified",
            PrefixSourceUpdateStatus.Failed => "Failed",
            _ => status.ToString()
        };

    private static string FormatUpdateStatus(
        PrefixUpdateCheckStatus status) =>
        status switch
        {
            PrefixUpdateCheckStatus.Current => "Current",
            PrefixUpdateCheckStatus.UpdateAvailable => "Update Available",
            PrefixUpdateCheckStatus.Unknown => "Unknown",
            PrefixUpdateCheckStatus.Failed => "Failed",
            _ => status.ToString()
        };

    private static string FormatTimestamp(
        DateTimeOffset? timestamp) =>
        timestamp is null
            ? "-"
            : Format(timestamp.Value);

    private static string ShortenHash(
        string? hash)
    {
        if (string.IsNullOrEmpty(hash))
        {
            return "-";
        }

        return hash.Length <= 8
            ? hash
            : hash[..8] + "...";
    }

    private static string FormatChange(
        int? count) =>
        count is null ? "-" : count.Value.ToString();

    private static string FormatChanged(
        PrefixSourceChangeSummary? summary) =>
        summary is null
            ? "-"
            : summary.HasChanges ? "Yes" : "No";

    private static string YesNo(
        bool? enabled) =>
        enabled switch
        {
            null => "Unknown",
            true => "Enabled",
            false => "Disabled"
        };

    private static string Format(
        DateTimeOffset timestamp) =>
        timestamp.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss UTC");
}
