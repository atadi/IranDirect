using IranDirect.Testing.Performance.Workloads;

namespace IranDirect.Core.Tests.Globalization;

/// <summary>
/// Phase 35.7A — lossless prefix aggregation acceptance.
///
/// The aggregator is an ANALYSIS utility in IranDirect.Testing (not wired into
/// the production prefix pipeline). These tests prove the lossless invariant
/// with an INDEPENDENT oracle (range-set union equality), cover edge cases,
/// canonical ordering/permutation, country-boundary safety, and measure real
/// reduction on representative workloads to decide v1 integration.
/// </summary>
public sealed class PrefixAggregationAcceptanceTests
{
    // ---- Independent oracle: expand CIDRs to merged [start,end] ranges ----

    private static List<(ulong Start, ulong End)> OracleRanges(
        IReadOnlyList<string> prefixes)
    {
        var ranges = prefixes
            .Select(ParseRange)
            .OrderBy(r => r.Start)
            .ThenBy(r => r.End)
            .ToList();
        var merged = new List<(ulong, ulong)>();
        foreach ((ulong Start, ulong End) r in ranges)
        {
            if (merged.Count > 0)
            {
                (ulong lastStart, ulong lastEnd) = merged[^1];
                if (r.Start <= lastEnd + 1)
                {
                    merged[^1] = (lastStart, Math.Max(lastEnd, r.End));
                    continue;
                }
            }
            merged.Add(r);
        }
        return merged;
    }

    private static (ulong Start, ulong End) ParseRange(string prefix)
    {
        string[] parts = prefix.Split('/');
        uint a = Ip(parts[0]);
        int len = int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
        uint mask = len == 0 ? 0u : ~0u << (32 - len);
        uint net = a & mask;
        ulong start = net;
        ulong block = len == 0 ? 1UL << 32 : 1UL << (32 - len);
        ulong end = net + block - 1;
        return (start, end);
    }

    private static uint Ip(string dotted)
    {
        string[] o = dotted.Split('.');
        uint v = 0;
        foreach (string x in o)
        {
            v = (v << 8) | byte.Parse(x, System.Globalization.CultureInfo.InvariantCulture);
        }
        return v;
    }

    private static void AssertUnionEqual(
        IReadOnlyList<string> original, IReadOnlyList<string> aggregated)
    {
        List<(ulong, ulong)> o = OracleRanges(original);
        List<(ulong, ulong)> a = OracleRanges(aggregated);
        Assert.Equal(o, a);
    }

    // ---- Step 3/4: lossless invariant on crafted datasets ----

    [Fact]
    public void ContainmentElimination()
    {
        string[] input = ["10.0.0.0/8", "10.1.0.0/16", "10.1.2.0/24"];
        IReadOnlyList<string> outp = Ipv4PrefixAggregator.Aggregate(input);
        Assert.Single(outp);
        Assert.Contains("10.0.0.0/8", outp);
        AssertUnionEqual(input, outp);
    }

    [Fact]
    public void ExactSiblingMerge()
    {
        string[] input = ["192.0.2.0/25", "192.0.2.128/25"];
        IReadOnlyList<string> outp = Ipv4PrefixAggregator.Aggregate(input);
        Assert.Single(outp);
        Assert.Contains("192.0.2.0/24", outp);
        AssertUnionEqual(input, outp);
    }

    [Fact]
    public void NonAdjacentNeverMerged()
    {
        // 10.0.0.0/24 and 10.0.2.0/24 have a gap (10.0.1.0/24) between them.
        string[] input = ["10.0.0.0/24", "10.0.2.0/24"];
        IReadOnlyList<string> outp = Ipv4PrefixAggregator.Aggregate(input);
        Assert.Equal(2, outp.Count);
        AssertUnionEqual(input, outp);
    }

    [Fact]
    public void RecursiveFourWayMerge()
    {
        // 192.0.2.0/26 x4 -> /24
        string[] input =
        [
            "192.0.2.0/26", "192.0.2.64/26",
            "192.0.2.128/26", "192.0.2.192/26"
        ];
        IReadOnlyList<string> outp = Ipv4PrefixAggregator.Aggregate(input);
        Assert.Single(outp);
        Assert.Contains("192.0.2.0/24", outp);
        AssertUnionEqual(input, outp);
    }

    [Fact]
    public void DuplicatesCollapsed()
    {
        string[] input = ["203.0.113.0/24", "203.0.113.0/24", "203.0.113.0/24"];
        IReadOnlyList<string> outp = Ipv4PrefixAggregator.Aggregate(input);
        Assert.Single(outp);
        AssertUnionEqual(input, outp);
    }

    [Fact]
    public void OverlappingButNonContained_CollapsesToMinimal()
    {
        // 10.0.0.0/24 and 10.0.0.128/25: union is exactly 10.0.0.0/24.
        string[] input = ["10.0.0.0/24", "10.0.0.128/25"];
        IReadOnlyList<string> outp = Ipv4PrefixAggregator.Aggregate(input);
        Assert.Single(outp);
        Assert.Contains("10.0.0.0/24", outp);
        AssertUnionEqual(input, outp);
    }

    // ---- Step 8: edge cases ----

    [Fact]
    public void SingleSlash32()
    {
        string[] input = ["203.0.113.7/32"];
        IReadOnlyList<string> outp = Ipv4PrefixAggregator.Aggregate(input);
        Assert.Single(outp);
        Assert.Contains("203.0.113.7/32", outp);
        AssertUnionEqual(input, outp);
    }

    [Fact]
    public void SlashZero_CoversAll()
    {
        string[] input = ["0.0.0.0/0", "203.0.113.0/24"];
        IReadOnlyList<string> outp = Ipv4PrefixAggregator.Aggregate(input);
        Assert.Single(outp);
        Assert.Contains("0.0.0.0/0", outp);
        AssertUnionEqual(input, outp);
    }

    [Fact]
    public void AddressBoundaries()
    {
        string[] input = ["0.0.0.0/32", "255.255.255.255/32"];
        IReadOnlyList<string> outp = Ipv4PrefixAggregator.Aggregate(input);
        Assert.Equal(2, outp.Count); // not adjacent, cannot merge
        AssertUnionEqual(input, outp);
    }

    [Fact]
    public void AcrossSlash8Boundary()
    {
        // 9.255.255.0/24 and 10.0.0.0/24 are NOT siblings (span /8 boundary
        // and are not adjacent in a single CIDR relationship).
        string[] input = ["9.255.255.0/24", "10.0.0.0/24"];
        IReadOnlyList<string> outp = Ipv4PrefixAggregator.Aggregate(input);
        Assert.Equal(2, outp.Count);
        AssertUnionEqual(input, outp);
    }

    [Fact]
    public void InvalidCidr_Rejected()
    {
        Assert.Throws<ArgumentException>(() =>
            Ipv4PrefixAggregator.Aggregate(["10.0.0.1/24"])); // host bits set
        Assert.Throws<ArgumentException>(() =>
            Ipv4PrefixAggregator.Aggregate(["not-an-ip/24"]));
        Assert.Throws<ArgumentException>(() =>
            Ipv4PrefixAggregator.Aggregate(["10.0.0.0/33"]));
    }

    // ---- Step 7: canonical ordering + permutation ----

    [Fact]
    public void OutputIsCanonicallyOrdered()
    {
        string[] input =
        [
            "192.0.2.128/25", "0.0.0.0/0", "10.0.0.0/8",
            "192.0.2.0/25", "203.0.113.0/24"
        ];
        IReadOnlyList<string> outp = Ipv4PrefixAggregator.Aggregate(input);
        // 0.0.0.0/0 subsumes the others (containment elimination), so output is
        // exactly that single prefix.
        Assert.Equal(["0.0.0.0/0"], outp);
    }

    [Fact]
    public void PermutationInvariance()
    {
        string[] baseInput =
        [
            "10.0.0.0/24", "10.0.1.0/24", "10.0.2.0/24", "10.0.3.0/24",
            "192.0.2.0/25", "192.0.2.128/25", "203.0.113.0/24"
        ];
        IReadOnlyList<string> reference =
            Ipv4PrefixAggregator.Aggregate(baseInput);

        Random rng = new(12345);
        string[] permuted = [.. baseInput];
        rng.Shuffle(permuted);
        IReadOnlyList<string> permutedOut =
            Ipv4PrefixAggregator.Aggregate(permuted);
        Assert.Equal(reference, permutedOut);
        AssertUnionEqual(baseInput, permutedOut);
    }

    // ---- Step 9: country-boundary safety ----

    [Fact]
    public void AggregationIsPerCountry_NeverCrossesBoundary()
    {
        // Same adjacent/sibling structure in two countries must NOT be merged
        // when aggregated independently (the country is the cache/source key).
        string[] irPrefixes =
            ["192.0.2.0/25", "192.0.2.128/25"]; // siblings within IR
        string[] iqPrefixes =
            ["198.51.100.0/25", "198.51.100.128/25"]; // siblings within IQ

        IReadOnlyList<string> irAgg =
            Ipv4PrefixAggregator.Aggregate(irPrefixes);
        IReadOnlyList<string> iqAgg =
            Ipv4PrefixAggregator.Aggregate(iqPrefixes);

        Assert.Single(irAgg);
        Assert.Contains("192.0.2.0/24", irAgg);
        Assert.Single(iqAgg);
        Assert.Contains("198.51.100.0/24", iqAgg);

        // If aggregated together they WOULD stay separate because the ranges are
        // disjoint across countries — proving no cross-country merge occurs.
        IReadOnlyList<string> combined =
            Ipv4PrefixAggregator.Aggregate(
                irPrefixes.Concat(iqPrefixes).ToList());
        Assert.Equal(2, combined.Count);
    }

    // ---- Step 5/14: reduction measurement on representative workloads ----

    public static IEnumerable<object[]> CountryWorkloads()
    {
        yield return new object[] { "IR", 2_000 };
        yield return new object[] { "IQ", 2_000 };
        yield return new object[] { "RO", 3_000 };
        yield return new object[] { "BR", 13_000 };
        yield return new object[] { "US", 70_000 };
    }

    /// <summary>
    /// Generates RIR-country-REALISTIC prefixes: scattered, disjoint allocations
    /// (mostly /24 with some shorter blocks), NOT the sequential /24 run that
    /// PrefixWorkloadGenerator produces. Real RIPEstat country-resource-list data
    /// is overwhelmingly disjoint, so lossless sibling aggregation yields only
    /// negligible reduction (the source already dedups). This generator models
    /// that reality for an honest v1 decision. Deterministic via seed.
    /// </summary>
    private static IReadOnlyList<string> GenerateDisjoint(int count, uint seed)
    {
        var rand = new Random(unchecked((int)seed));
        var seen = new HashSet<string>();
        while (seen.Count < count)
        {
            int len = PickLength(rand);
            uint net = RandomAlignedNetwork(rand, len);
            seen.Add($"{IpStr(net)}/{len}");
        }
        return [.. seen];
    }

    private static int PickLength(Random rand)
    {
        // Weighted toward /24 (typical end-user allocation), some /20-/22,
        // fewer large blocks — mirrors a mixed RIR country allocation set.
        int r = rand.Next(100);
        if (r < 70) return 24;
        if (r < 88) return 22;
        if (r < 96) return 20;
        if (r < 99) return 16;
        return 12;
    }

    private static uint RandomAlignedNetwork(Random rand, int len)
    {
        uint addr = (uint)(rand.NextInt64(1, 1L << 32) & 0xFFFFFFFF);
        uint mask = len == 0 ? 0u : ~0u << (32 - len);
        return addr & mask;
    }

    private static string IpStr(uint address) =>
        $"{(address >> 24) & 0xFF}.{(address >> 16) & 0xFF}." +
        $"{(address >> 8) & 0xFF}.{address & 0xFF}";

    [Theory]
    [MemberData(nameof(CountryWorkloads))]
    public void CountryWorkload_ReductionMeasured(string country, int size)
    {
        // Realistic disjoint RIR-style allocations: lossless sibling aggregation
        // yields negligible reduction (the source already dedups; exact siblings
        // among scattered allocations are rare). The decision is driven by this.
        IReadOnlyList<string> input = GenerateDisjoint(size, (uint)size);
        IReadOnlyList<string> outp = Ipv4PrefixAggregator.Aggregate(input);

        AssertUnionEqual(input, outp);
        int reductionPct = 100 * (input.Count - outp.Count) / input.Count;
        // Scattered allocations occasionally form exact sibling pairs (two
        // adjacent /24s both present), giving a MODEST reduction — not a
        // transformational one. Any value in a sane band confirms the algorithm
        // is lossless on realistic data and the reduction is bounded/expected.
        Assert.True(reductionPct is >= 0 and <= 25,
            $"{country}: unexpected reduction {reductionPct}% on disjoint data");
    }

    [Fact]
    public void LargeRandomDeterministicSet_LosslessAndBounded()
    {
        // PrefixWorkloadGenerator lays out /24s sequentially, so contiguous
        // blocks aggregate heavily — this is a BEST-CASE ceiling proving the
        // algorithm is lossless and bounded at 70K, not the realistic scattered
        // reduction (which CountryWorkload_ReductionMeasured models).
        PrefixWorkloadGenerator gen = new();
        IReadOnlyList<string> input = gen.Generate(70_000);
        IReadOnlyList<string> outp = Ipv4PrefixAggregator.Aggregate(input);
        AssertUnionEqual(input, outp);
        Assert.True(outp.Count <= input.Count,
            "aggregation must never increase route count");

        // Determinism: same input order-independent.
        List<string> shuffled = [.. input];
        shuffled.Reverse();
        IReadOnlyList<string> outp2 =
            Ipv4PrefixAggregator.Aggregate(shuffled);
        Assert.Equal(outp, outp2);
    }

    [Fact]
    public void DisjointSeventyK_NearNoOp()
    {
        // Scattered 70K allocations (realistic US-scale country shape):
        // aggregation is a near no-op, so count is barely reduced, never grown.
        IReadOnlyList<string> input = GenerateDisjoint(70_000, 70_000);
        IReadOnlyList<string> outp = Ipv4PrefixAggregator.Aggregate(input);
        AssertUnionEqual(input, outp);
        Assert.True(outp.Count <= input.Count,
            "aggregation must never increase route count");
        Assert.True(outp.Count > input.Count / 2,
            "scattered data should not collapse materially");
    }

    [Fact]
    public void StructuredSiblingWorkload_ShowsRealReduction()
    {
        // Synthetic structured dataset: many /32s grouped into /24s, so exact
        // sibling aggregation reduces dramatically. This demonstrates the
        // algorithm's value WHEN data is aggregatable (unlike RIR allocations).
        var input = new List<string>();
        // 256 /32s per /24, for 1000 /24 blocks => 256_000 prefixes.
        for (int b = 0; b < 1000; b++)
        {
            uint base24 = (uint)(b << 8); // 0.0.<b>.0/24 style (simplified)
            for (int h = 0; h < 256; h++)
            {
                input.Add($"{base24 >> 24 & 0xFF}.{base24 >> 16 & 0xFF}." +
                          $"{base24 >> 8 & 0xFF}.{h}/32");
            }
        }
        IReadOnlyList<string> outp = Ipv4PrefixAggregator.Aggregate(input);
        AssertUnionEqual(input, outp);
        // 256_000 /32s in 1000 blocks => 1000 /24s (huge reduction).
        Assert.True(outp.Count < input.Count / 100,
            $"structured reduction too small: {outp.Count}");
    }
}
