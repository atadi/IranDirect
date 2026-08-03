using System.Net;

namespace IranDirect.Core.Tests.Performance.Workloads;

public sealed class PrefixWorkloadGeneratorTests
{
    private readonly PrefixWorkloadGenerator _generator = new();

    [Theory]
    [InlineData(RuntimeWorkloadSize.Scale1K)]
    [InlineData(RuntimeWorkloadSize.Scale2K)]
    [InlineData(RuntimeWorkloadSize.Scale5K)]
    [InlineData(RuntimeWorkloadSize.Scale10K)]
    [InlineData(RuntimeWorkloadSize.Scale25K)]
    [InlineData(RuntimeWorkloadSize.Scale50K)]
    public void Generate_SupportedScales_ProduceExactCounts(RuntimeWorkloadSize size)
    {
        IReadOnlyList<string> prefixes = _generator.Generate((int)size);

        Assert.Equal((int)size, prefixes.Count);
    }

    [Fact]
    public void Generate_ProducesDistinctNetworks()
    {
        IReadOnlyList<string> prefixes = _generator.Generate(50_000);

        Assert.Equal(50_000, prefixes.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Generate_AllPrefixesAreValidNonReservedCidrs()
    {
        IReadOnlyList<string> prefixes = _generator.Generate(10_000);

        foreach (string prefix in prefixes)
        {
            AssertValidCidr(prefix);
        }
    }

    [Fact]
    public void Generate_SpansMoreThan256DistinctNetworks()
    {
        IReadOnlyList<string> prefixes = _generator.Generate(1_000);

        var octets = prefixes
            .Select(prefix => prefix.Split('.')[..3])
            .ToArray();

        int distinctSecondOctets = octets
            .Select(parts => int.Parse(parts[1]))
            .Distinct()
            .Count();

        int distinctThirdOctets = octets
            .Select(parts => int.Parse(parts[2]))
            .Distinct()
            .Count();

        Assert.Equal(1_000, prefixes.Count);
        Assert.True(
            distinctSecondOctets >= 3,
            "1,000 /24 networks must span at least three second-octet values.");
        Assert.Equal(256, distinctThirdOctets);
    }

    [Fact]
    public void Generate_SpansMoreThan65536Entries()
    {
        const int count = 70_000;

        IReadOnlyList<string> prefixes = _generator.Generate(count);

        Assert.Equal(count, prefixes.Count);
        Assert.Equal(count, prefixes.Distinct(StringComparer.Ordinal).Count());

        int distinctFirstOctets = prefixes
            .Select(prefix => int.Parse(prefix[..prefix.IndexOf('.')]))
            .Distinct()
            .Count();

        Assert.True(
            distinctFirstOctets >= 2,
            "Beyond 65,536 entries generation must advance into new first octets.");
    }

    [Fact]
    public void ToCidr_IsArithmeticStableOrdering()
    {
        Assert.Equal("1.0.0.0/24", PrefixWorkloadGenerator.ToCidr(0));
        Assert.Equal("1.0.255.0/24", PrefixWorkloadGenerator.ToCidr(255));
        Assert.Equal("1.1.0.0/24", PrefixWorkloadGenerator.ToCidr(256));
        Assert.Equal("1.255.255.0/24", PrefixWorkloadGenerator.ToCidr(65_535));
        Assert.Equal("2.0.0.0/24", PrefixWorkloadGenerator.ToCidr(65_536));
        Assert.Equal("128.0.0.0/24", PrefixWorkloadGenerator.ToCidr(126 * 65_536));
        Assert.Equal("223.0.0.0/24", PrefixWorkloadGenerator.ToCidr(221 * 65_536));
    }

    [Fact]
    public void ToCidr_NeverProducesReservedFirstOctets()
    {
        for (int index = 0; index < 14_548_992; index += 1_000)
        {
            string prefix = PrefixWorkloadGenerator.ToCidr(index);
            int first = int.Parse(prefix[..prefix.IndexOf('.')]);

            Assert.InRange(first, 1, 223);
            Assert.NotEqual(127, first);
            Assert.True(first < 224);
        }
    }

    [Fact]
    public void GenerateEndpointPrefixes_ProducesValidDistinctCidrs()
    {
        IReadOnlyList<string> endpoints = _generator.GenerateEndpointPrefixes(10_000);

        Assert.Equal(10_000, endpoints.Count);
        Assert.Equal(10_000, endpoints.Distinct(StringComparer.Ordinal).Count());

        foreach (string endpoint in endpoints)
        {
            AssertValidCidr(endpoint);

            string[] octets = endpoint[..endpoint.IndexOf('/')].Split('.');
            int fourth = int.Parse(octets[3]);

            Assert.InRange(fourth, 1, 253);
        }
    }

    [Fact]
    public void Generate_NegativeCount_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => _generator.Generate(-1));
    }

    [Fact]
    public void Generate_CountBeyondCapacity_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => _generator.Generate(PrefixWorkloadGenerator.MaxSupportedPrefixCount + 1));
    }

    private static void AssertValidCidr(string cidr)
    {
        int slash = cidr.IndexOf('/');
        Assert.True(slash > 0);

        string address = cidr[..slash];
        string length = cidr[(slash + 1)..];

        byte[] octets = IPAddress.Parse(address).GetAddressBytes();
        Assert.Equal(4, octets.Length);
        Assert.NotEqual(0, octets[0]);
        Assert.InRange(octets[0], 1, 223);
        Assert.NotEqual(127, octets[0]);

        int prefixLength = int.Parse(length);
        Assert.InRange(prefixLength, 1, 32);
    }
}
