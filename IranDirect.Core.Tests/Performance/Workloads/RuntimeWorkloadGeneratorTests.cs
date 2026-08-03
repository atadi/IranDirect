using System.Diagnostics;
using IranDirect.Core.Runtime;
using IranDirect.Testing.Performance.Workloads;

namespace IranDirect.Core.Tests.Performance.Workloads;

public sealed class RuntimeWorkloadGeneratorTests
{
    private readonly RuntimeWorkloadGenerator _generator = new();

    public static IEnumerable<object[]> AllScenarios() =>
        Enum.GetValues<RuntimeWorkloadScenario>()
            .Select(scenario => new object[] { scenario });

    public static IEnumerable<object[]> AllSizes() =>
        Enum.GetValues<RuntimeWorkloadSize>()
            .Select(size => new object[] { size });

    public static IEnumerable<object[]> AllScenariosAndSizes() =>
        from scenario in Enum.GetValues<RuntimeWorkloadScenario>()
        from size in Enum.GetValues<RuntimeWorkloadSize>()
        select new object[] { scenario, size };

    [Fact]
    public void Sizes_MapToDocumentedScales()
    {
        Assert.Equal(1_000, (int)RuntimeWorkloadSize.Scale1K);
        Assert.Equal(2_000, (int)RuntimeWorkloadSize.Scale2K);
        Assert.Equal(5_000, (int)RuntimeWorkloadSize.Scale5K);
        Assert.Equal(10_000, (int)RuntimeWorkloadSize.Scale10K);
        Assert.Equal(25_000, (int)RuntimeWorkloadSize.Scale25K);
        Assert.Equal(50_000, (int)RuntimeWorkloadSize.Scale50K);
    }

    [Theory]
    [MemberData(nameof(AllSizes))]
    public void Generate_AllSizes_ProduceExactScale(RuntimeWorkloadSize size)
    {
        RuntimeWorkload workload =
            _generator.Generate(RuntimeWorkloadScenario.AllPresent, size);
        int expected = (int)size;

        Assert.Equal(expected, workload.DesiredPrefixes.Count);
        Assert.Equal(expected, workload.ObservedRoutes.Count);
        Assert.Equal(expected, workload.RouteInventory.Routes.Count);
        Assert.Equal(expected, workload.ExpectedUnchangedCount);
    }

    [Theory]
    [MemberData(nameof(AllScenarios))]
    public void Generate_ProducesExactStructuralCounts(RuntimeWorkloadScenario scenario)
    {
        const int count = (int)RuntimeWorkloadSize.Scale1K;

        RuntimeWorkload workload =
            _generator.Generate(scenario, RuntimeWorkloadSize.Scale1K);

        switch (scenario)
        {
            case RuntimeWorkloadScenario.AllMissing:
                Assert.Equal(count, workload.DesiredPrefixes.Count);
                Assert.Empty(workload.ObservedRoutes);
                Assert.Empty(workload.RouteInventory.Routes);
                Assert.Empty(workload.DesiredEndpointRoutes);
                Assert.Empty(workload.VpnEndpoints);
                Assert.Empty(workload.VpnEndpointInventory.Endpoints);
                break;

            case RuntimeWorkloadScenario.AllPresent:
                Assert.Equal(count, workload.DesiredPrefixes.Count);
                Assert.Equal(count, workload.ObservedRoutes.Count);
                Assert.Equal(count, workload.RouteInventory.Routes.Count);
                break;

            case RuntimeWorkloadScenario.AllObsolete:
                Assert.Empty(workload.DesiredPrefixes);
                Assert.Equal(count, workload.ObservedRoutes.Count);
                Assert.Equal(count, workload.RouteInventory.Routes.Count);
                break;

            case RuntimeWorkloadScenario.Mixed:
                int missing = count * 15 / 100;
                int unchangedAndMismatchCount = count - missing;
                Assert.Equal(unchangedAndMismatchCount, workload.DesiredPrefixes.Count);
                Assert.Equal(unchangedAndMismatchCount, workload.ObservedRoutes.Count);
                Assert.Equal(unchangedAndMismatchCount, workload.RouteInventory.Routes.Count);
                break;

            case RuntimeWorkloadScenario.EndpointMixed:
                int prefixCount = count * 50 / 100;
                int endpointCount = count - prefixCount;
                int unchangedEndpoints = endpointCount * 50 / 100;
                int missingEndpoints = endpointCount * 25 / 100;
                int obsoleteEndpoints =
                    endpointCount - unchangedEndpoints - missingEndpoints;
                Assert.Equal(prefixCount, workload.DesiredPrefixes.Count);
                Assert.Equal(
                    prefixCount + unchangedEndpoints + obsoleteEndpoints,
                    workload.ObservedRoutes.Count);
                Assert.Equal(prefixCount, workload.RouteInventory.Routes.Count);
                Assert.Equal(
                    unchangedEndpoints + missingEndpoints,
                    workload.DesiredEndpointRoutes.Count);
                Assert.Equal(
                    unchangedEndpoints + obsoleteEndpoints,
                    workload.VpnEndpoints.Count);
                Assert.Equal(
                    unchangedEndpoints + obsoleteEndpoints,
                    workload.VpnEndpointInventory.Endpoints.Count);
                break;

            case RuntimeWorkloadScenario.DuplicateInput:
                Assert.Equal(count * 2, workload.DesiredPrefixes.Count);
                Assert.Empty(workload.ObservedRoutes);
                Assert.Equal(count, workload.ExpectedAddedCount);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(scenario));
        }
    }

    [Theory]
    [MemberData(nameof(AllScenarios))]
    public void ExpectedCounts_MatchRecomputedCounts(
        RuntimeWorkloadScenario scenario)
    {
        foreach (RuntimeWorkloadSize size in Enum.GetValues<RuntimeWorkloadSize>())
        {
            RuntimeWorkload workload = _generator.Generate(scenario, size);

            (int added, int removed, int unchanged) =
                RuntimeWorkloadVerifier.RecomputeCounts(workload);

            Assert.Equal(workload.ExpectedAddedCount, added);
            Assert.Equal(workload.ExpectedRemovedCount, removed);
            Assert.Equal(workload.ExpectedUnchangedCount, unchanged);
        }
    }

    [Fact]
    public void Generate_SameInputs_ProducesIdenticalOrderedWorkloads()
    {
        const int seed = 12345;

        foreach (RuntimeWorkloadScenario scenario in Enum.GetValues<RuntimeWorkloadScenario>())
        {
            RuntimeWorkload first =
                _generator.Generate(scenario, RuntimeWorkloadSize.Scale5K, seed);
            RuntimeWorkload second =
                _generator.Generate(scenario, RuntimeWorkloadSize.Scale5K, seed);

            Assert.True(
                first.DesiredPrefixes.SequenceEqual(second.DesiredPrefixes));
            Assert.True(
                first.ObservedRoutes.SequenceEqual(second.ObservedRoutes));
            Assert.True(
                first.DesiredEndpointRoutes.SequenceEqual(second.DesiredEndpointRoutes));
            Assert.True(
                first.VpnEndpoints.SequenceEqual(second.VpnEndpoints));
            Assert.True(
                first.RouteInventory.Routes.SequenceEqual(second.RouteInventory.Routes));
            Assert.True(
                first.VpnEndpointInventory.Endpoints.SequenceEqual(
                    second.VpnEndpointInventory.Endpoints));
            Assert.Equal(first.ExpectedAddedCount, second.ExpectedAddedCount);
            Assert.Equal(first.ExpectedRemovedCount, second.ExpectedRemovedCount);
            Assert.Equal(first.ExpectedUnchangedCount, second.ExpectedUnchangedCount);
        }
    }

    [Fact]
    public void RepeatedGeneration_DoesNotShareMutableState()
    {
        RuntimeWorkload first =
            _generator.Generate(RuntimeWorkloadScenario.AllPresent, RuntimeWorkloadSize.Scale2K);
        RuntimeWorkload second =
            _generator.Generate(RuntimeWorkloadScenario.AllPresent, RuntimeWorkloadSize.Scale2K);
        RuntimeWorkload third =
            _generator.Generate(RuntimeWorkloadScenario.AllPresent, RuntimeWorkloadSize.Scale2K);

        Assert.False(ReferenceEquals(first.DesiredPrefixes, second.DesiredPrefixes));
        Assert.False(ReferenceEquals(first.ObservedRoutes, second.ObservedRoutes));
        Assert.False(ReferenceEquals(first.RouteInventory, second.RouteInventory));
        Assert.False(ReferenceEquals(first.DesiredPrefixes[0], second.DesiredPrefixes[0]));

        Assert.True(first.DesiredPrefixes.SequenceEqual(third.DesiredPrefixes));
        Assert.True(first.ObservedRoutes.SequenceEqual(third.ObservedRoutes));
    }

    [Fact]
    public void Generate_DifferentScenarios_ProduceIntendedStructuralDifferences()
    {
        RuntimeWorkload missing =
            _generator.Generate(RuntimeWorkloadScenario.AllMissing, RuntimeWorkloadSize.Scale1K);
        RuntimeWorkload present =
            _generator.Generate(RuntimeWorkloadScenario.AllPresent, RuntimeWorkloadSize.Scale1K);
        RuntimeWorkload obsolete =
            _generator.Generate(RuntimeWorkloadScenario.AllObsolete, RuntimeWorkloadSize.Scale1K);

        Assert.Empty(missing.ObservedRoutes);
        Assert.NotEmpty(present.ObservedRoutes);
        Assert.NotEmpty(obsolete.ObservedRoutes);

        Assert.Empty(obsolete.DesiredPrefixes);
        Assert.True(
            missing.DesiredPrefixes.SequenceEqual(present.DesiredPrefixes));

        Assert.Equal(1_000, missing.ExpectedAddedCount);
        Assert.Equal(0, present.ExpectedAddedCount);
        Assert.Equal(0, obsolete.ExpectedAddedCount);
        Assert.Equal(1_000, obsolete.ExpectedRemovedCount);
    }

    [Fact]
    public void Generate_DifferentSeeds_ProduceDifferentPersonalities()
    {
        RuntimeWorkload seedOne =
            _generator.Generate(RuntimeWorkloadScenario.AllPresent, RuntimeWorkloadSize.Scale1K, 1);
        RuntimeWorkload seedTwo =
            _generator.Generate(RuntimeWorkloadScenario.AllPresent, RuntimeWorkloadSize.Scale1K, 2);

        Assert.Equal(1, seedOne.Seed);
        Assert.Equal(2, seedTwo.Seed);
        Assert.NotEqual(
            seedOne.DesiredPrefixes[0].Gateway,
            seedTwo.DesiredPrefixes[0].Gateway);
    }

    [Fact]
    public void Mixed_Scenario_StableProportions()
    {
        RuntimeWorkload workload =
            _generator.Generate(RuntimeWorkloadScenario.Mixed, RuntimeWorkloadSize.Scale50K);

        Assert.Equal(20_000, workload.ExpectedUnchangedCount);
        Assert.Equal(22_500, workload.ExpectedAddedCount);
        Assert.Equal(22_500, workload.ExpectedRemovedCount);
    }

    [Fact]
    public void Mixed_MismatchedEntries_ProduceIntendedDifferences()
    {
        RuntimeWorkload workload =
            _generator.Generate(RuntimeWorkloadScenario.Mixed, RuntimeWorkloadSize.Scale1K);

        var desiredByPrefix = workload.DesiredPrefixes
            .ToDictionary(
                route => route.DestinationPrefix,
                StringComparer.OrdinalIgnoreCase);

        int unchanged = 0;
        int mismatchedGateway = 0;
        int mismatchedInterface = 0;

        foreach (ObservedRoute observed in workload.ObservedRoutes)
        {
            if (!desiredByPrefix.TryGetValue(
                    observed.DestinationPrefix,
                    out DesiredPrefixRoute? desired))
            {
                continue;
            }

            if (string.Equals(
                    desired.Identity,
                    observed.Identity,
                    StringComparison.OrdinalIgnoreCase))
            {
                unchanged++;
            }
            else if (!string.Equals(
                         desired.Gateway,
                         observed.NextHop,
                         StringComparison.Ordinal))
            {
                mismatchedGateway++;
            }
            else
            {
                mismatchedInterface++;
            }
        }

        Assert.Equal(400, unchanged);
        Assert.Equal(150, mismatchedGateway);
        Assert.Equal(150, mismatchedInterface);
    }

    [Fact]
    public void EndpointMixed_IncludesAllEndpointChangeKinds()
    {
        RuntimeWorkload workload =
            _generator.Generate(RuntimeWorkloadScenario.EndpointMixed, RuntimeWorkloadSize.Scale2K);

        Assert.NotEmpty(workload.DesiredEndpointRoutes);
        Assert.NotEmpty(workload.VpnEndpoints);
        Assert.True(workload.ExpectedAddedCount > 0);
        Assert.True(workload.ExpectedRemovedCount > 0);
        Assert.True(workload.ExpectedUnchangedCount > 0);

        Assert.True(
            workload.VpnEndpointInventory.Endpoints.All(
                item => item.AddedByIranDirect));
    }

    [Theory]
    [MemberData(nameof(AllScenarios))]
    public void NoDuplicatePrefixes_ExceptDuplicateInput(RuntimeWorkloadScenario scenario)
    {
        RuntimeWorkload workload =
            _generator.Generate(scenario, RuntimeWorkloadSize.Scale10K);

        bool duplicates = RuntimeWorkloadVerifier.HasDuplicatePrefixes(workload);

        Assert.Equal(scenario == RuntimeWorkloadScenario.DuplicateInput, duplicates);
    }

    [Fact]
    public void Generate_AllGeneratedCidrsParseSuccessfully()
    {
        RuntimeWorkload workload =
            _generator.Generate(RuntimeWorkloadScenario.Mixed, RuntimeWorkloadSize.Scale10K);

        foreach (string prefix in workload.DesiredPrefixes
                     .Select(route => route.DestinationPrefix))
        {
            AssertValidCidr(prefix);
        }

        foreach (string prefix in workload.ObservedRoutes
                     .Select(route => route.DestinationPrefix))
        {
            AssertValidCidr(prefix);
        }

        foreach (string prefix in workload.RouteInventory.Routes
                     .Select(item => item.DestinationPrefix))
        {
            AssertValidCidr(prefix);
        }
    }

    [Fact]
    public void Generate_WorkloadMetadata_IsRecorded()
    {
        RuntimeWorkload workload =
            _generator.Generate(RuntimeWorkloadScenario.Mixed, RuntimeWorkloadSize.Scale1K);

        Assert.Equal(RuntimeWorkloadScenario.Mixed, workload.Scenario);
        Assert.Equal(RuntimeWorkloadSize.Scale1K, workload.Size);
        Assert.Equal(RuntimeWorkloadGenerator.DefaultSeed, workload.Seed);
        Assert.Equal(RuntimeWorkloadGenerator.GeneratorVersion, workload.GeneratorVersion);
    }

    [Fact]
    public void GenerateScale50K_CompletesWithoutExcessiveDelay()
    {
        var stopwatch = Stopwatch.StartNew();

        RuntimeWorkload workload =
            _generator.Generate(RuntimeWorkloadScenario.Mixed, RuntimeWorkloadSize.Scale50K);

        stopwatch.Stop();

        Assert.Equal(42_500, workload.DesiredPrefixes.Count);
        Assert.Equal(42_500, workload.ObservedRoutes.Count);
        Assert.Equal(42_500, workload.RouteInventory.Routes.Count);
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(30),
            $"Scale50K generation took {stopwatch.Elapsed}.");
    }

    private static void AssertValidCidr(string cidr)
    {
        int slash = cidr.IndexOf('/');
        Assert.True(slash > 0);

        string address = cidr[..slash];
        string length = cidr[(slash + 1)..];

        byte[] octets = System.Net.IPAddress.Parse(address).GetAddressBytes();
        Assert.Equal(4, octets.Length);
        Assert.InRange(octets[0], 1, 223);

        int prefixLength = int.Parse(length);
        Assert.InRange(prefixLength, 1, 32);
    }
}
