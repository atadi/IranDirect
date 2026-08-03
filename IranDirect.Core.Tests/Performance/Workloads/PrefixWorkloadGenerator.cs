namespace IranDirect.Core.Tests.Performance.Workloads;

public sealed class PrefixWorkloadGenerator
{
    public const int MaxSupportedPrefixCount = 222 * 65_536;

    public IReadOnlyList<string> Generate(int count)
    {
        if (count < 0 || count > MaxSupportedPrefixCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(count),
                count,
                $"Count must be between 0 and {MaxSupportedPrefixCount}.");
        }

        var prefixes = new List<string>(count);
        for (int i = 0; i < count; i++)
        {
            prefixes.Add(ToCidr(i));
        }

        return prefixes;
    }

    public IReadOnlyList<string> GenerateEndpointPrefixes(int count)
    {
        if (count < 0 || count > MaxSupportedPrefixCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(count),
                count,
                $"Count must be between 0 and {MaxSupportedPrefixCount}.");
        }

        var prefixes = new List<string>(count);
        for (int i = 0; i < count; i++)
        {
            prefixes.Add(ToEndpointCidr(i));
        }

        return prefixes;
    }

    public static string ToCidr(int index)
    {
        if (index < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        int first = FirstOctet(index / 65_536);
        int second = (index / 256) % 256;
        int third = index % 256;
        return $"{first}.{second}.{third}.0/24";
    }

    public static string ToEndpointCidr(int index)
    {
        if (index < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        int first = FirstOctet(index / 65_536);
        int second = (index / 256) % 256;
        int third = index % 256;
        int fourth = 1 + ((index * 7 + 3) % 253);
        return $"{first}.{second}.{third}.{fourth}/32";
    }

    private static int FirstOctet(int group) =>
        group < 126 ? group + 1 : group + 2;
}
