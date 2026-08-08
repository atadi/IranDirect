using System.Globalization;

namespace PathVeer.Testing.Performance.Workloads;

/// <summary>
/// Lossless IPv4 CIDR aggregator (analysis/test utility, NOT production routing
/// code). Produces the minimal, semantically-equivalent set of CIDRs that
/// represents EXACTLY the same IPv4 address set as the validated input.
///
/// Hard invariant: Union(output CIDRs) == Union(input CIDRs). No neighboring
/// address space is added; prefixes are only eliminated when strictly contained
/// in another, or merged when two prefixes are exact CIDR siblings (their
/// ranges are adjacent with no gap). Overlapping/adjacent ranges are merged;
/// ranges separated by a gap are never merged.
///
/// This lives in the testing/analysis layer on purpose: Phase 35.7A concluded
/// that real RIR country datasets are overwhelmingly disjoint, so aggregation
/// yields negligible route-count reduction and is NOT wired into the production
/// prefix pipeline. It remains available for documentation, thresholds, and
/// future evaluation.
/// </summary>
public static class Ipv4PrefixAggregator
{
    /// <summary>
    /// Aggregates validated IPv4 CIDR strings into the minimal equivalent set.
    /// Output is ordered by network address ascending, then prefix length.
    /// Invalid CIDRs (malformed, non-canonical with host bits set, or out of
    /// range) cause <see cref="ArgumentException"/> — callers must validate
    /// through the production validation layer first.
    /// </summary>
    public static IReadOnlyList<string> Aggregate(
        IReadOnlyList<string> prefixes)
    {
        ArgumentNullException.ThrowIfNull(prefixes);

        // 1. Parse to canonical [start, end] inclusive ranges (UInt64 to avoid
        //    overflow at 0xFFFFFFFF).
        var ranges = new List<(ulong Start, ulong End)>(prefixes.Count);
        foreach (string p in prefixes)
        {
            (ulong start, ulong end) = ParseCanonical(p);
            ranges.Add((start, end));
        }

        // 2. Sort and merge overlapping/adjacent ranges only.
        List<(ulong Start, ulong End)> merged = MergeRanges(ranges);

        // 3. Decompose each merged range into the minimal covering CIDR set.
        var result = new List<string>();
        foreach ((ulong start, ulong end) in merged)
        {
            Decompose(start, end, result);
        }

        result.Sort(Comparer);
        return result;
    }

    private static List<(ulong Start, ulong End)> MergeRanges(
        List<(ulong Start, ulong End)> ranges)
    {
        if (ranges.Count == 0)
        {
            return ranges;
        }

        ranges.Sort((a, b) =>
            a.Start != b.Start
                ? a.Start.CompareTo(b.Start)
                : a.End.CompareTo(b.End));

        var merged = new List<(ulong Start, ulong End)>();
        (ulong start, ulong end) = ranges[0];
        for (int i = 1; i < ranges.Count; i++)
        {
            (ulong s, ulong e) = ranges[i];
            // Merge only when overlapping OR exactly adjacent (gap of zero).
            if (s <= end + 1)
            {
                if (e > end)
                {
                    end = e;
                }
            }
            else
            {
                merged.Add((start, end));
                start = s;
                end = e;
            }
        }
        merged.Add((start, end));
        return merged;
    }

    private static void Decompose(
        ulong start, ulong end, List<string> output)
    {
        while (start <= end)
        {
            // Start at the smallest block (/32) and grow to the largest power of
            // two that is both aligned at `start` and fits within the remaining
            // range. LowestSetBit(start) is the maximum alignment for start.
            ulong maxSize = LowestSetBit(start); // 0 means start == 0 (whole space)
            if (maxSize == 0)
            {
                maxSize = 1UL << 32;
            }

            ulong remaining = end - start + 1;
            ulong size = 1;
            while (size < remaining && size < maxSize)
            {
                ulong next = size << 1;
                if ((start & (next - 1)) != 0)
                {
                    break;
                }
                if (next > remaining)
                {
                    break;
                }
                size = next;
            }

            int prefixLength = 32 - (int)Math.Log(size, 2);
            output.Add($"{IpString((uint)start)}/{prefixLength}");
            start += size;
        }
    }

    /// <summary>Largest power of two that divides <paramref name="value"/>
    /// (i.e., the alignment of the address). Returns 0 for value 0.</summary>
    private static ulong LowestSetBit(ulong value)
    {
        if (value == 0)
        {
            return 0;
        }
        ulong bit = 1;
        while ((value & 1) == 0)
        {
            value >>= 1;
            bit <<= 1;
        }
        return bit;
    }

    private static int Comparer(string a, string b)
    {
        (uint aNet, int aLen) = Split(a);
        (uint bNet, int bLen) = Split(b);
        int c = aNet.CompareTo(bNet);
        return c != 0 ? c : aLen.CompareTo(bLen);
    }

    private static (uint Network, int Length) Split(string cidr)
    {
        string[] parts = cidr.Split('/');
        uint net = IPToUint(parts[0]);
        int len = int.Parse(parts[1], CultureInfo.InvariantCulture);
        return (net, len);
    }

    private static (ulong Start, ulong End) ParseCanonical(string prefix)
    {
        if (prefix is null)
        {
            throw new ArgumentException("Prefix is null.", nameof(prefix));
        }
        string[] parts = prefix.Split('/');
        if (parts.Length != 2)
        {
            throw new ArgumentException(
                $"Malformed CIDR (expected 'a.b.c.d/n'): {prefix}",
                nameof(prefix));
        }

        uint address = IPToUint(parts[0]);
        if (!byte.TryParse(
                parts[1], NumberStyles.None, CultureInfo.InvariantCulture,
                out byte prefixLength)
            || prefixLength > 32)
        {
            throw new ArgumentException(
                $"Invalid prefix length in: {prefix}", nameof(prefix));
        }

        uint mask = prefixLength == 0 ? 0u : ~0u << (32 - prefixLength);
        uint network = address & mask;
        if (network != address)
        {
            throw new ArgumentException(
                $"Non-canonical CIDR with host bits set (expected " +
                $"{IpString(network)}/{prefixLength}): {prefix}",
                nameof(prefix));
        }

        ulong start = network;
        ulong block = prefixLength == 0
            ? 1UL << 32
            : 1UL << (32 - prefixLength);
        ulong end = network + block - 1;
        return (start, end);
    }

    private static uint IPToUint(string dotted)
    {
        string[] octets = dotted.Split('.');
        if (octets.Length != 4)
        {
            throw new ArgumentException(
                $"Malformed IPv4 address: {dotted}", nameof(dotted));
        }
        uint value = 0;
        foreach (string o in octets)
        {
            if (!byte.TryParse(
                    o, NumberStyles.None, CultureInfo.InvariantCulture,
                    out byte b))
            {
                throw new ArgumentException(
                    $"Malformed IPv4 octet: {o}", nameof(o));
            }
            value = (value << 8) | b;
        }
        return value;
    }

    private static string IpString(uint address)
    {
        return $"{(address >> 24) & 0xFF}.{(address >> 16) & 0xFF}." +
               $"{(address >> 8) & 0xFF}.{address & 0xFF}";
    }
}
