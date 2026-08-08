using PathVeer.Core.CustomRoutes;

namespace PathVeer.Core.Cli;

public static class CustomRouteCliRenderer
{
    public static IEnumerable<string> RenderList(
        IReadOnlyList<CustomRouteEntry> entries)
    {
        yield return "=== Custom Routes ===";

        if (entries.Count == 0)
        {
            yield return "No custom routes configured.";
            yield break;
        }

        yield return
            $"{"ID",-38} " +
            $"{"Type",-10} " +
            $"{"Enabled",-8} " +
            $"{"Value",-24} " +
            $"Description";

        foreach (CustomRouteEntry entry in entries)
        {
            yield return
                $"{entry.Id,-38} " +
                $"{entry.Type,-10} " +
                $"{(entry.Enabled ? "yes" : "no"),-8} " +
                $"{entry.Value,-24} " +
                $"{entry.Description}";
        }
    }

    public static IEnumerable<string> RenderResolve(
        CustomRouteResolutionResult result)
    {
        yield return "=== Custom Routes: Resolution ===";
        yield return $"Resolved prefixes: {result.Prefixes.Count}";

        foreach (string prefix in result.Prefixes)
        {
            yield return prefix;
        }

        yield return $"Failures: {result.Failures.Count}";

        foreach (CustomRouteResolutionFailure failure
                 in result.Failures)
        {
            yield return
                $"- [{failure.Type}] {failure.Value}: " +
                $"{failure.Reason}";
        }
    }

    public static IEnumerable<string> RenderCacheStatus(
        IReadOnlyList<CustomRouteDnsCacheStatus> statuses)
    {
        yield return "=== Custom Routes: Cache Status ===";

        if (statuses.Count == 0)
        {
            yield return "No domains configured.";
            yield break;
        }

        yield return
            $"{"Domain",-28} " +
            $"{"Enabled",-8} " +
            $"{"State",-9} " +
            $"{"Addresses",-22} " +
            $"{"Last Success",-22} " +
            $"{"Expires",-22} " +
            $"{"Stale Until",-22} " +
            $"Last Error";

        foreach (CustomRouteDnsCacheStatus status in statuses)
        {
            yield return
                $"{status.Domain,-28} " +
                $"{(status.Enabled ? "yes" : "no"),-8} " +
                $"{status.State,-9} " +
                $"{FormatAddresses(status.IPv4Addresses),-22} " +
                $"{Format(status.LastSucceededAt),-22} " +
                $"{Format(status.ExpiresAt),-22} " +
                $"{Format(status.StaleUntil),-22} " +
                $"{status.LastError}";
        }
    }

    private static string FormatAddresses(
        IReadOnlyList<string> addresses) =>
        addresses.Count == 0
            ? "-"
            : string.Join(", ", addresses);

    private static string Format(
        DateTimeOffset? timestamp) =>
        timestamp is null
            ? "-"
            : timestamp.Value.UtcDateTime
                .ToString("yyyy-MM-dd HH:mm:ss");
}
