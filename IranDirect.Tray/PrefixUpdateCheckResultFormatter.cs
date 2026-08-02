using IranDirect.Core.Prefixes;

namespace IranDirect.Tray;

public static class PrefixUpdateCheckResultFormatter
{
    public static string BuildResultText(
        PrefixUpdateMonitorSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        PrefixUpdateCheckResult? result =
            snapshot.CurrentResult;

        string status = result?.Status switch
        {
            PrefixUpdateCheckStatus.Current => "Current",
            PrefixUpdateCheckStatus.UpdateAvailable =>
                "Update Available",
            PrefixUpdateCheckStatus.Unknown => "Unknown",
            PrefixUpdateCheckStatus.Failed => "Failed",
            _ => "Unknown"
        };

        string text =
            $"Status: {status}\n" +
            $"Checked: {FormatTimestamp(result?.CheckedAt)}\n" +
            $"Consecutive failures: " +
            $"{snapshot.ConsecutiveFailures}";

        if (!string.IsNullOrWhiteSpace(result?.Reason))
        {
            text += $"\nReason: {result.Reason}";
        }

        return text;
    }

    public static bool IsFailure(
        PrefixUpdateMonitorSnapshot snapshot) =>
        snapshot.CurrentResult?.Status ==
        PrefixUpdateCheckStatus.Failed;

    private static string FormatTimestamp(
        DateTimeOffset? timestamp) =>
        timestamp is null
            ? "-"
            : timestamp.Value.ToLocalTime().ToString(
                "yyyy-MM-dd HH:mm:ss");
}
