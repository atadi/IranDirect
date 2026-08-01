using IranDirect.Core.CustomRoutes;

namespace IranDirect.Core.Cli;

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
}
