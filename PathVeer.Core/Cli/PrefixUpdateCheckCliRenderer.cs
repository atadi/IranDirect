using PathVeer.Core.Prefixes;

namespace PathVeer.Core.Cli;

public static class PrefixUpdateCheckCliRenderer
{
    public static IEnumerable<string> Render(
        PrefixUpdateMonitorSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        PrefixUpdateCheckResult? result =
            snapshot.CurrentResult;
        PrefixUpdateCheckRemoteMetadata? remote =
            result?.RemoteMetadata;

        yield return "=== Prefix Update Check ===";
        yield return
            $"Status: {FormatStatus(result?.Status)}";
        yield return
            $"Checked at: {FormatTimestamp(result?.CheckedAt)}";
        yield return
            "Last successful check: " +
            FormatTimestamp(snapshot.LastSuccessfulCheckAt);
        yield return
            $"Consecutive failures: " +
            $"{snapshot.ConsecutiveFailures}";
        yield return
            $"Remote ETag: {remote?.ETag ?? "-"}";
        yield return
            "Remote Last Modified: " +
            FormatTimestamp(remote?.LastModified);
        yield return
            "Remote Content Length: " +
            (remote?.ContentLength?.ToString() ?? "-");
        yield return
            $"Reason: " +
            (string.IsNullOrWhiteSpace(result?.Reason)
                ? "-"
                : result!.Reason);
    }

    public static string FormatStatus(
        PrefixUpdateCheckStatus? status) =>
        status switch
        {
            PrefixUpdateCheckStatus.Current => "Current",
            PrefixUpdateCheckStatus.UpdateAvailable =>
                "Update Available",
            PrefixUpdateCheckStatus.Unknown => "Unknown",
            PrefixUpdateCheckStatus.Failed => "Failed",
            null => "Unknown",
            _ => status.Value.ToString()
        };

    private static string FormatTimestamp(
        DateTimeOffset? timestamp) =>
        timestamp is null
            ? "-"
            : timestamp.Value.UtcDateTime.ToString(
                "yyyy-MM-dd HH:mm:ss UTC");
}
