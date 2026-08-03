using IranDirect.Core.Routing;
using IranDirect.Core.Runtime;
using IranDirect.Core.Vpn;

namespace IranDirect.Testing.Performance.Workloads;

public sealed class RuntimeWorkloadGenerator
{
    public const int DefaultSeed = 20260803;

    public const string GeneratorVersion = "1.0";

    private readonly PrefixWorkloadGenerator _prefixes;

    private readonly RouteWorkloadGenerator _routes;

    public RuntimeWorkloadGenerator()
        : this(new PrefixWorkloadGenerator(), new RouteWorkloadGenerator())
    {
    }

    public RuntimeWorkloadGenerator(
        PrefixWorkloadGenerator prefixes,
        RouteWorkloadGenerator routes)
    {
        ArgumentNullException.ThrowIfNull(prefixes);
        ArgumentNullException.ThrowIfNull(routes);

        _prefixes = prefixes;
        _routes = routes;
    }

    public RuntimeWorkload Generate(
        RuntimeWorkloadScenario scenario,
        RuntimeWorkloadSize size) =>
        Generate(scenario, size, DefaultSeed);

    public RuntimeWorkload Generate(
        RuntimeWorkloadScenario scenario,
        RuntimeWorkloadSize size,
        int seed)
    {
        int count = (int)size;

        return scenario switch
        {
            RuntimeWorkloadScenario.AllMissing =>
                GenerateAllMissing(count, seed),
            RuntimeWorkloadScenario.AllPresent =>
                GenerateAllPresent(count, seed),
            RuntimeWorkloadScenario.AllObsolete =>
                GenerateAllObsolete(count, seed),
            RuntimeWorkloadScenario.Mixed =>
                GenerateMixed(count, seed),
            RuntimeWorkloadScenario.EndpointMixed =>
                GenerateEndpointMixed(count, seed),
            RuntimeWorkloadScenario.DuplicateInput =>
                GenerateDuplicateInput(count, seed),
            _ => throw new ArgumentOutOfRangeException(
                nameof(scenario),
                scenario,
                null)
        };
    }

    private RuntimeWorkload GenerateAllMissing(int count, int seed)
    {
        IReadOnlyList<string> prefixes = _prefixes.Generate(count);
        var desired = new List<DesiredPrefixRoute>(count);
        for (int i = 0; i < count; i++)
        {
            desired.Add(_routes.DesiredPrefix(prefixes[i], i, seed));
        }

        return Finish(
            RuntimeWorkloadScenario.AllMissing,
            count,
            seed,
            desired,
            [],
            new RouteInventory(),
            [],
            [],
            new VpnEndpointInventory(),
            expectedAdded: count,
            expectedRemoved: 0,
            expectedUnchanged: 0);
    }

    private RuntimeWorkload GenerateAllPresent(int count, int seed)
    {
        IReadOnlyList<string> prefixes = _prefixes.Generate(count);
        var desired = new List<DesiredPrefixRoute>(count);
        var observed = new List<ObservedRoute>(count);
        var inventory = new List<RouteInventoryItem>(count);
        for (int i = 0; i < count; i++)
        {
            desired.Add(_routes.DesiredPrefix(prefixes[i], i, seed));
            observed.Add(_routes.ObservedPrefix(prefixes[i], i, seed));
            inventory.Add(_routes.PrefixInventoryItem(prefixes[i], i, seed));
        }

        return Finish(
            RuntimeWorkloadScenario.AllPresent,
            count,
            seed,
            desired,
            observed,
            new RouteInventory { Routes = inventory },
            [],
            [],
            new VpnEndpointInventory(),
            expectedAdded: 0,
            expectedRemoved: 0,
            expectedUnchanged: count);
    }

    private RuntimeWorkload GenerateAllObsolete(int count, int seed)
    {
        IReadOnlyList<string> prefixes = _prefixes.Generate(count);
        var observed = new List<ObservedRoute>(count);
        var inventory = new List<RouteInventoryItem>(count);
        for (int i = 0; i < count; i++)
        {
            observed.Add(_routes.ObservedPrefix(prefixes[i], i, seed));
            inventory.Add(_routes.PrefixInventoryItem(prefixes[i], i, seed));
        }

        return Finish(
            RuntimeWorkloadScenario.AllObsolete,
            count,
            seed,
            [],
            observed,
            new RouteInventory { Routes = inventory },
            [],
            [],
            new VpnEndpointInventory(),
            expectedAdded: 0,
            expectedRemoved: count,
            expectedUnchanged: 0);
    }

    private RuntimeWorkload GenerateMixed(int count, int seed)
    {
        (int unchanged, int missing, int obsolete,
            int mismatchedGateway, int mismatchedInterface) =
            SplitMixed(count);

        IReadOnlyList<string> prefixes = _prefixes.Generate(count);

        var desired = new List<DesiredPrefixRoute>(
            unchanged + missing + mismatchedGateway + mismatchedInterface);
        var observed = new List<ObservedRoute>(
            unchanged + obsolete + mismatchedGateway + mismatchedInterface);
        var inventory = new List<RouteInventoryItem>(
            unchanged + obsolete + mismatchedGateway + mismatchedInterface);

        int index = 0;
        int unchangedEnd = index + unchanged;
        int missingEnd = unchangedEnd + missing;
        int obsoleteEnd = missingEnd + obsolete;
        int mismatchedGatewayEnd = obsoleteEnd + mismatchedGateway;

        for (; index < unchangedEnd; index++)
        {
            desired.Add(_routes.DesiredPrefix(prefixes[index], index, seed));
            observed.Add(_routes.ObservedPrefix(prefixes[index], index, seed));
            inventory.Add(_routes.PrefixInventoryItem(prefixes[index], index, seed));
        }

        for (; index < missingEnd; index++)
        {
            desired.Add(_routes.DesiredPrefix(prefixes[index], index, seed));
        }

        for (; index < obsoleteEnd; index++)
        {
            observed.Add(_routes.ObservedPrefix(prefixes[index], index, seed));
            inventory.Add(_routes.PrefixInventoryItem(prefixes[index], index, seed));
        }

        for (; index < mismatchedGatewayEnd; index++)
        {
            desired.Add(_routes.DesiredPrefix(prefixes[index], index, seed));
            observed.Add(
                _routes.ObservedPrefixMismatchedGateway(prefixes[index], index, seed));
            inventory.Add(
                _routes.PrefixInventoryItemMismatchedGateway(prefixes[index], index, seed));
        }

        for (; index < count; index++)
        {
            desired.Add(_routes.DesiredPrefix(prefixes[index], index, seed));
            observed.Add(
                _routes.ObservedPrefixMismatchedInterface(prefixes[index], index, seed));
            inventory.Add(
                _routes.PrefixInventoryItemMismatchedInterface(prefixes[index], index, seed));
        }

        return Finish(
            RuntimeWorkloadScenario.Mixed,
            count,
            seed,
            desired,
            observed,
            new RouteInventory { Routes = inventory },
            [],
            [],
            new VpnEndpointInventory(),
            expectedAdded: missing + mismatchedGateway + mismatchedInterface,
            expectedRemoved: obsolete + mismatchedGateway + mismatchedInterface,
            expectedUnchanged: unchanged);
    }

    private RuntimeWorkload GenerateEndpointMixed(int count, int seed)
    {
        int prefixCount = count * 50 / 100;
        int endpointCount = count - prefixCount;
        int unchangedEndpoints = endpointCount * 50 / 100;
        int missingEndpoints = endpointCount * 25 / 100;
        int obsoleteEndpoints =
            endpointCount - unchangedEndpoints - missingEndpoints;

        IReadOnlyList<string> prefixes = _prefixes.Generate(prefixCount);
        IReadOnlyList<string> endpointPrefixes =
            _prefixes.GenerateEndpointPrefixes(endpointCount);

        var desiredPrefixes = new List<DesiredPrefixRoute>(prefixCount);
        var observedPrefixes = new List<ObservedRoute>(prefixCount);
        var prefixInventory = new List<RouteInventoryItem>(prefixCount);
        for (int i = 0; i < prefixCount; i++)
        {
            desiredPrefixes.Add(_routes.DesiredPrefix(prefixes[i], i, seed));
            observedPrefixes.Add(_routes.ObservedPrefix(prefixes[i], i, seed));
            prefixInventory.Add(_routes.PrefixInventoryItem(prefixes[i], i, seed));
        }

        var desiredEndpoints =
            new List<DesiredEndpointRoute>(unchangedEndpoints + missingEndpoints);
        var observedEndpoints =
            new List<ObservedRoute>(unchangedEndpoints + obsoleteEndpoints);
        var vpnEndpoints =
            new List<ObservedVpnEndpoint>(unchangedEndpoints + obsoleteEndpoints);
        var vpnInventory =
            new List<VpnEndpointInventoryItem>(unchangedEndpoints + obsoleteEndpoints);

        int endpointIndex = 0;
        int unchangedEnd = endpointIndex + unchangedEndpoints;
        int missingEnd = unchangedEnd + missingEndpoints;

        for (; endpointIndex < unchangedEnd; endpointIndex++)
        {
            desiredEndpoints.Add(
                _routes.DesiredEndpoint(endpointPrefixes[endpointIndex], endpointIndex, seed));
            observedEndpoints.Add(
                _routes.ObservedEndpointRoute(endpointPrefixes[endpointIndex], endpointIndex, seed));
            vpnEndpoints.Add(
                _routes.VpnEndpoint(endpointPrefixes[endpointIndex], endpointIndex, seed));
            vpnInventory.Add(
                _routes.EndpointInventoryItem(
                    endpointPrefixes[endpointIndex],
                    endpointIndex,
                    seed,
                    addedByIranDirect: true));
        }

        for (; endpointIndex < missingEnd; endpointIndex++)
        {
            desiredEndpoints.Add(
                _routes.DesiredEndpoint(endpointPrefixes[endpointIndex], endpointIndex, seed));
        }

        for (; endpointIndex < endpointCount; endpointIndex++)
        {
            observedEndpoints.Add(
                _routes.ObservedEndpointRoute(endpointPrefixes[endpointIndex], endpointIndex, seed));
            vpnEndpoints.Add(
                _routes.VpnEndpoint(endpointPrefixes[endpointIndex], endpointIndex, seed));
            vpnInventory.Add(
                _routes.EndpointInventoryItem(
                    endpointPrefixes[endpointIndex],
                    endpointIndex,
                    seed,
                    addedByIranDirect: true));
        }

        var observed = new List<ObservedRoute>(
            observedPrefixes.Count + observedEndpoints.Count);
        observed.AddRange(observedPrefixes);
        observed.AddRange(observedEndpoints);

        return Finish(
            RuntimeWorkloadScenario.EndpointMixed,
            count,
            seed,
            desiredPrefixes,
            observed,
            new RouteInventory { Routes = prefixInventory },
            desiredEndpoints,
            vpnEndpoints,
            new VpnEndpointInventory { Endpoints = vpnInventory },
            expectedAdded: missingEndpoints,
            expectedRemoved: obsoleteEndpoints,
            expectedUnchanged: prefixCount + unchangedEndpoints);
    }

    private RuntimeWorkload GenerateDuplicateInput(int count, int seed)
    {
        IReadOnlyList<string> prefixes = _prefixes.Generate(count);
        var desired = new List<DesiredPrefixRoute>(count * 2);
        for (int i = 0; i < count; i++)
        {
            desired.Add(_routes.DesiredPrefix(prefixes[i], i, seed));
            desired.Add(_routes.DesiredPrefix(prefixes[i], i, seed));
        }

        return Finish(
            RuntimeWorkloadScenario.DuplicateInput,
            count,
            seed,
            desired,
            [],
            new RouteInventory(),
            [],
            [],
            new VpnEndpointInventory(),
            expectedAdded: count,
            expectedRemoved: 0,
            expectedUnchanged: 0);
    }

    private static (
        int Unchanged,
        int Missing,
        int Obsolete,
        int MismatchedGateway,
        int MismatchedInterface) SplitMixed(int count)
    {
        int unchanged = count * 40 / 100;
        int missing = count * 15 / 100;
        int obsolete = count * 15 / 100;
        int mismatchedGateway = count * 15 / 100;
        int mismatchedInterface =
            count - unchanged - missing - obsolete - mismatchedGateway;
        return (unchanged, missing, obsolete, mismatchedGateway, mismatchedInterface);
    }

    private static RuntimeWorkload Finish(
        RuntimeWorkloadScenario scenario,
        int count,
        int seed,
        IReadOnlyList<DesiredPrefixRoute> desired,
        IReadOnlyList<ObservedRoute> observed,
        RouteInventory inventory,
        IReadOnlyList<DesiredEndpointRoute> desiredEndpoints,
        IReadOnlyList<ObservedVpnEndpoint> vpnEndpoints,
        VpnEndpointInventory vpnInventory,
        int expectedAdded,
        int expectedRemoved,
        int expectedUnchanged) =>
        new()
        {
            Scenario = scenario,
            Size = (RuntimeWorkloadSize)count,
            Seed = seed,
            GeneratorVersion = GeneratorVersion,
            DesiredPrefixes = desired.ToArray(),
            ObservedRoutes = observed.ToArray(),
            RouteInventory = inventory,
            DesiredEndpointRoutes = desiredEndpoints.ToArray(),
            VpnEndpoints = vpnEndpoints.ToArray(),
            VpnEndpointInventory = vpnInventory,
            ExpectedAddedCount = expectedAdded,
            ExpectedRemovedCount = expectedRemoved,
            ExpectedUnchangedCount = expectedUnchanged
        };
}
