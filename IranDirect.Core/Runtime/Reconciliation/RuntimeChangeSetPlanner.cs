namespace IranDirect.Core.Runtime.Reconciliation;

public sealed class RuntimeChangeSetPlanner : IRuntimeChangeSetPlanner
{
    public RuntimeChangeSet Plan(
        RuntimePlanSnapshot snapshot,
        RuntimeRouteOwnership ownership)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(ownership);

        if (!snapshot.Desired.CanReconcile)
        {
            return new RuntimeChangeSet();
        }

        // One observed-route lookup, first-occurrence wins (matching the
        // prior GroupBy(...).First() semantics). This single map serves
        // both the add passes (membership test) and the removal passes
        // (value lookup), instead of being rebuilt per category.
        Dictionary<string, ObservedRoute> observedRoutes =
            BuildFirstOccurrenceLookup(
                snapshot.Observed.Routes,
                route => route.Identity);

        // Desired membership sets (case-insensitive) are now filled by
        // the add passes themselves. Identity is a computed interpolated
        // property, so materializing it in a separate pre-pass and again
        // during classification allocated every identity string twice and
        // required a second per-category "already added" set with exactly
        // the same first-occurrence semantics. A single traversal that
        // uses the set's own Add result as the duplicate test produces
        // the identical membership set and the identical change order
        // while allocating each identity once.
        HashSet<string> desiredEndpointIds =
            new(
                snapshot.Desired.EndpointRoutes.Count,
                StringComparer.OrdinalIgnoreCase);
        HashSet<string> desiredPrefixIds =
            new(
                snapshot.Desired.PrefixRoutes.Count,
                StringComparer.OrdinalIgnoreCase);

        List<RuntimeChange> changes = [];

        AddMissingEndpointRoutes(
            snapshot.Desired.EndpointRoutes,
            observedRoutes,
            desiredEndpointIds,
            changes);

        int addEndpointEnd = changes.Count;

        RemoveUndesiredOwnedEndpointRoutes(
            desiredEndpointIds,
            observedRoutes,
            ownership,
            changes);

        int removeEndpointEnd = changes.Count;

        AddMissingPrefixRoutes(
            snapshot.Desired.PrefixRoutes,
            observedRoutes,
            desiredPrefixIds,
            changes);

        int addPrefixEnd = changes.Count;

        RemoveUndesiredOwnedPrefixRoutes(
            desiredPrefixIds,
            observedRoutes,
            ownership,
            changes);

        return new RuntimeChangeSet
        {
            Changes = SortAuthoritative(
                changes,
                addEndpointEnd,
                removeEndpointEnd,
                addPrefixEnd)
        };
    }

    /// <summary>
    /// Reproduces <c>OrderBy(Kind).ThenBy(Identity, OrdinalIgnoreCase)</c>
    /// without the LINQ sort pipeline. The four passes above append in
    /// ascending <see cref="RuntimeChangeKind"/> order
    /// (AddEndpointRoute, RemoveEndpointRoute, AddPrefixRoute,
    /// RemovePrefixRoute), so the buffer is already partitioned by kind
    /// and only needs an identity sort inside each partition. Identities
    /// are unique within a partition — the add passes dedupe by identity
    /// and the removal passes iterate an identity set — so the ordering
    /// is a total order and does not depend on sort stability.
    /// </summary>
    private static RuntimeChange[] SortAuthoritative(
        List<RuntimeChange> changes,
        int addEndpointEnd,
        int removeEndpointEnd,
        int addPrefixEnd)
    {
        RuntimeChange[] ordered = [.. changes];

        SortByIdentity(ordered, 0, addEndpointEnd);
        SortByIdentity(ordered, addEndpointEnd, removeEndpointEnd);
        SortByIdentity(ordered, removeEndpointEnd, addPrefixEnd);
        SortByIdentity(ordered, addPrefixEnd, ordered.Length);

        return ordered;
    }

    private static void SortByIdentity(
        RuntimeChange[] ordered,
        int start,
        int end)
    {
        int length = end - start;
        if (length < 2)
        {
            return;
        }

        Array.Sort(
            ordered,
            start,
            length,
            IdentityComparer.Instance);
    }

    private sealed class IdentityComparer : IComparer<RuntimeChange>
    {
        internal static readonly IdentityComparer Instance = new();

        public int Compare(RuntimeChange? x, RuntimeChange? y) =>
            StringComparer.OrdinalIgnoreCase.Compare(
                x!.Identity,
                y!.Identity);
    }

    private static Dictionary<string, T> BuildFirstOccurrenceLookup<T>(
        IReadOnlyList<T> items,
        Func<T, string> keySelector)
    {
        var lookup = new Dictionary<string, T>(
            items.Count,
            StringComparer.OrdinalIgnoreCase);

        foreach (T item in items)
        {
            string key = keySelector(item);
            if (!lookup.ContainsKey(key))
            {
                lookup.Add(key, item);
            }
        }

        return lookup;
    }

    private static void AddMissingEndpointRoutes(
        IReadOnlyList<DesiredEndpointRoute> desired,
        IReadOnlyDictionary<string, ObservedRoute> observed,
        HashSet<string> desiredIds,
        ICollection<RuntimeChange> changes)
    {
        foreach (DesiredEndpointRoute route in desired)
        {
            string identity = route.Identity;

            // First-occurrence semantics: only the first desired route
            // for an identity can produce a change, and every desired
            // identity still lands in the membership set for the
            // subsequent removal pass.
            if (!desiredIds.Add(identity) ||
                observed.ContainsKey(identity))
            {
                continue;
            }

            changes.Add(
                new RuntimeChange
                {
                    Kind =
                        RuntimeChangeKind.AddEndpointRoute,
                    Identity = identity,
                    DestinationPrefix =
                        route.DestinationPrefix,
                    Gateway = route.Gateway,
                    InterfaceIndex =
                        route.InterfaceIndex,
                    Metric = route.Metric,
                    Description =
                        $"Protect VPN endpoint " +
                        $"{route.DestinationPrefix} through " +
                        $"{route.Gateway}."
                });
        }
    }

    private static void RemoveUndesiredOwnedEndpointRoutes(
        HashSet<string> desiredIds,
        IReadOnlyDictionary<string, ObservedRoute> observed,
        RuntimeRouteOwnership ownership,
        ICollection<RuntimeChange> changes)
    {
        foreach (string identity
                 in ownership.EndpointRouteIdentities)
        {
            if (desiredIds.Contains(identity) ||
                !observed.TryGetValue(
                    identity,
                    out ObservedRoute? route))
            {
                continue;
            }

            changes.Add(
                CreateRemoval(
                    RuntimeChangeKind.RemoveEndpointRoute,
                    route,
                    "Remove an obsolete IranDirect-owned " +
                    "VPN endpoint route."));
        }
    }

    private static void AddMissingPrefixRoutes(
        IReadOnlyList<DesiredPrefixRoute> desired,
        IReadOnlyDictionary<string, ObservedRoute> observed,
        HashSet<string> desiredIds,
        ICollection<RuntimeChange> changes)
    {
        foreach (DesiredPrefixRoute route in desired)
        {
            string identity = route.Identity;

            if (!desiredIds.Add(identity) ||
                observed.ContainsKey(identity))
            {
                continue;
            }

            changes.Add(
                new RuntimeChange
                {
                    Kind =
                        RuntimeChangeKind.AddPrefixRoute,
                    Identity = identity,
                    DestinationPrefix =
                        route.DestinationPrefix,
                    Gateway = route.Gateway,
                    InterfaceIndex =
                        route.InterfaceIndex,
                    Metric = route.Metric,
                    Description =
                        $"Add direct prefix route " +
                        $"{route.DestinationPrefix} through " +
                        $"{route.Gateway}."
                });
        }
    }

    private static void RemoveUndesiredOwnedPrefixRoutes(
        HashSet<string> desiredIds,
        IReadOnlyDictionary<string, ObservedRoute> observed,
        RuntimeRouteOwnership ownership,
        ICollection<RuntimeChange> changes)
    {
        foreach (string identity
                 in ownership.PrefixRouteIdentities)
        {
            if (desiredIds.Contains(identity) ||
                !observed.TryGetValue(
                    identity,
                    out ObservedRoute? route))
            {
                continue;
            }

            changes.Add(
                CreateRemoval(
                    RuntimeChangeKind.RemovePrefixRoute,
                    route,
                    "Remove an obsolete IranDirect-owned " +
                    "direct prefix route."));
        }
    }

    private static RuntimeChange CreateRemoval(
        RuntimeChangeKind kind,
        ObservedRoute route,
        string description) =>
        new()
        {
            Kind = kind,
            Identity = route.Identity,
            DestinationPrefix =
                route.DestinationPrefix,
            Gateway = route.NextHop,
            InterfaceIndex =
                route.InterfaceIndex,
            Metric = route.Metric,
            Description = description
        };
}
