namespace IranDirect.Core.Runtime.Reconciliation;

public sealed class RuntimeChangeSetPlanner
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

        Dictionary<string, ObservedRoute> observedRoutes =
            snapshot.Observed.Routes
                .GroupBy(
                    route => route.Identity,
                    StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.First(),
                    StringComparer.OrdinalIgnoreCase);

        Dictionary<string, DesiredEndpointRoute>
            desiredEndpointRoutes =
                snapshot.Desired.EndpointRoutes
                    .GroupBy(
                        route => route.Identity,
                        StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        group => group.Key,
                        group => group.First(),
                        StringComparer.OrdinalIgnoreCase);

        Dictionary<string, DesiredPrefixRoute>
            desiredPrefixRoutes =
                snapshot.Desired.PrefixRoutes
                    .GroupBy(
                        route => route.Identity,
                        StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        group => group.Key,
                        group => group.First(),
                        StringComparer.OrdinalIgnoreCase);

        List<RuntimeChange> changes = [];

        AddMissingEndpointRoutes(
            desiredEndpointRoutes,
            observedRoutes,
            changes);

        RemoveUndesiredOwnedEndpointRoutes(
            desiredEndpointRoutes,
            observedRoutes,
            ownership,
            changes);

        AddMissingPrefixRoutes(
            desiredPrefixRoutes,
            observedRoutes,
            changes);

        RemoveUndesiredOwnedPrefixRoutes(
            desiredPrefixRoutes,
            observedRoutes,
            ownership,
            changes);

        return new RuntimeChangeSet
        {
            Changes = changes
                .OrderBy(change => change.Kind)
                .ThenBy(
                    change => change.Identity,
                    StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };
    }

    private static void AddMissingEndpointRoutes(
        IReadOnlyDictionary<string, DesiredEndpointRoute> desired,
        IReadOnlyDictionary<string, ObservedRoute> observed,
        ICollection<RuntimeChange> changes)
    {
        foreach ((string identity, DesiredEndpointRoute route)
                 in desired)
        {
            if (observed.ContainsKey(identity))
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
        IReadOnlyDictionary<string, DesiredEndpointRoute> desired,
        IReadOnlyDictionary<string, ObservedRoute> observed,
        RuntimeRouteOwnership ownership,
        ICollection<RuntimeChange> changes)
    {
        foreach (string identity
                 in ownership.EndpointRouteIdentities)
        {
            if (desired.ContainsKey(identity) ||
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
        IReadOnlyDictionary<string, DesiredPrefixRoute> desired,
        IReadOnlyDictionary<string, ObservedRoute> observed,
        ICollection<RuntimeChange> changes)
    {
        foreach ((string identity, DesiredPrefixRoute route)
                 in desired)
        {
            if (observed.ContainsKey(identity))
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
        IReadOnlyDictionary<string, DesiredPrefixRoute> desired,
        IReadOnlyDictionary<string, ObservedRoute> observed,
        RuntimeRouteOwnership ownership,
        ICollection<RuntimeChange> changes)
    {
        foreach (string identity
                 in ownership.PrefixRouteIdentities)
        {
            if (desired.ContainsKey(identity) ||
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
        string description)
    {
        return new RuntimeChange
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
}