namespace IranDirect.Testing.Performance.Workloads;

public static class RuntimeWorkloadVerifier
{
    public static (int Added, int Removed, int Unchanged) RecomputeCounts(
        RuntimeWorkload workload)
    {
        ArgumentNullException.ThrowIfNull(workload);

        var observed = workload.ObservedRoutes
            .GroupBy(
                route => route.Identity,
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First(),
                StringComparer.OrdinalIgnoreCase);

        var desiredPrefixIdentities = workload.DesiredPrefixes
            .Select(route => route.Identity)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var desiredEndpointIdentities = workload.DesiredEndpointRoutes
            .Select(route => route.Identity)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var ownedPrefixIdentities = workload.RouteInventory.Routes
            .Select(item => item.Identity)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var ownedEndpointIdentities = workload.VpnEndpointInventory.Endpoints
            .Where(item => item.AddedByIranDirect)
            .Select(item => item.Identity)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        int added = 0;
        int removed = 0;
        int unchanged = 0;

        foreach (string identity in desiredPrefixIdentities)
        {
            if (observed.ContainsKey(identity))
            {
                if (ownedPrefixIdentities.Contains(identity))
                {
                    unchanged++;
                }
            }
            else
            {
                added++;
            }
        }

        foreach (string identity in desiredEndpointIdentities)
        {
            if (observed.ContainsKey(identity))
            {
                if (ownedEndpointIdentities.Contains(identity))
                {
                    unchanged++;
                }
            }
            else
            {
                added++;
            }
        }

        foreach (string identity in ownedPrefixIdentities)
        {
            if (!desiredPrefixIdentities.Contains(identity) &&
                observed.ContainsKey(identity))
            {
                removed++;
            }
        }

        foreach (string identity in ownedEndpointIdentities)
        {
            if (!desiredEndpointIdentities.Contains(identity) &&
                observed.ContainsKey(identity))
            {
                removed++;
            }
        }

        return (added, removed, unchanged);
    }

    public static bool HasDuplicatePrefixes(RuntimeWorkload workload)
    {
        ArgumentNullException.ThrowIfNull(workload);

        return HasDuplicates(workload.DesiredPrefixes.Select(route => route.DestinationPrefix)) ||
            HasDuplicates(workload.ObservedRoutes.Select(route => route.DestinationPrefix)) ||
            HasDuplicates(workload.RouteInventory.Routes.Select(item => item.DestinationPrefix));
    }

    private static bool HasDuplicates(IEnumerable<string> values) =>
        values
            .GroupBy(
                value => value,
                StringComparer.OrdinalIgnoreCase)
            .Any(group => group.Count() > 1);
}
