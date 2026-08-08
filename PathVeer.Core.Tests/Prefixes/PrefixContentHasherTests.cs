using PathVeer.Core.Prefixes;

namespace PathVeer.Core.Tests.Prefixes;

public sealed class PrefixContentHasherTests
{
    [Fact]
    public void ComputeHash_IsDeterministic()
    {
        string[] prefixes =
        [
            "1.2.3.0/24",
            "10.0.0.0/8",
            "192.0.2.0/24"
        ];

        string first = PrefixContentHasher.ComputeHash(prefixes);
        string second = PrefixContentHasher.ComputeHash(prefixes);

        Assert.Equal(first, second);
        Assert.Matches("^[0-9a-f]{64}$", first);
    }

    [Fact]
    public void ComputeHash_InputOrderDoesNotAffectHash()
    {
        string[] forward =
        [
            "1.2.3.0/24",
            "10.0.0.0/8"
        ];
        string[] reversed =
        [
            "10.0.0.0/8",
            "1.2.3.0/24"
        ];

        Assert.Equal(
            PrefixContentHasher.ComputeHash(forward),
            PrefixContentHasher.ComputeHash(reversed));
    }

    [Fact]
    public void ComputeHash_CrlfVersusLfDoesNotAffectHash()
    {
        string[] crlf =
        [
            "1.2.3.0/24\r\n",
            "10.0.0.0/8\r\n"
        ];
        string[] lf =
        [
            "1.2.3.0/24",
            "10.0.0.0/8"
        ];

        Assert.Equal(
            PrefixContentHasher.ComputeHash(crlf),
            PrefixContentHasher.ComputeHash(lf));
    }

    [Fact]
    public void ComputeHash_SurroundingWhitespaceDoesNotAffectHash()
    {
        string[] padded =
        [
            "  1.2.3.0/24  ",
            "10.0.0.0/8"
        ];
        string[] clean =
        [
            "1.2.3.0/24",
            "10.0.0.0/8"
        ];

        Assert.Equal(
            PrefixContentHasher.ComputeHash(padded),
            PrefixContentHasher.ComputeHash(clean));
    }

    [Fact]
    public void ComputeHash_DuplicatePrefixesAreCanonicalized()
    {
        string[] duplicates =
        [
            "1.2.3.0/24",
            "1.2.3.0/24",
            "10.0.0.0/8"
        ];
        string[] unique =
        [
            "10.0.0.0/8",
            "1.2.3.0/24"
        ];

        Assert.Equal(
            PrefixContentHasher.ComputeHash(duplicates),
            PrefixContentHasher.ComputeHash(unique));
    }

    [Fact]
    public void ComputeHash_BlankLinesAreIgnored()
    {
        string[] withBlanks =
        [
            "1.2.3.0/24",
            "",
            "  ",
            "10.0.0.0/8"
        ];
        string[] clean =
        [
            "10.0.0.0/8",
            "1.2.3.0/24"
        ];

        Assert.Equal(
            PrefixContentHasher.ComputeHash(withBlanks),
            PrefixContentHasher.ComputeHash(clean));
    }

    [Fact]
    public void ComputeHash_ChangedDatasetChangesHash()
    {
        string[] first =
        [
            "1.2.3.0/24",
            "10.0.0.0/8"
        ];
        string[] second =
        [
            "1.2.3.0/24",
            "10.0.0.0/9"
        ];

        Assert.NotEqual(
            PrefixContentHasher.ComputeHash(first),
            PrefixContentHasher.ComputeHash(second));
    }

    [Fact]
    public void Canonicalize_ProducesOnePrefixPerLineWithLf()
    {
        string[] prefixes =
        [
            "10.0.0.0/8",
            "1.2.3.0/24"
        ];

        string canonical =
            PrefixContentHasher.Canonicalize(prefixes);

        Assert.Equal(
            "1.2.3.0/24\n10.0.0.0/8\n",
            canonical);
    }

    [Fact]
    public void ComputeHash_EmptyInput_IsStable()
    {
        string first = PrefixContentHasher.ComputeHash([]);
        string second = PrefixContentHasher.ComputeHash(
            Array.Empty<string>());

        Assert.Equal(first, second);
        Assert.Equal(64, first.Length);
    }
}
