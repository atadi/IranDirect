using IranDirect.Core.Configuration;

namespace IranDirect.Core.Tests.Configuration;

public sealed class DirectCountryCodeTests
{
    [Theory]
    [InlineData("IR", true)]
    [InlineData("ir", true)]   // lowercase normalized
    [InlineData("Iq", true)]   // mixed case normalized
    [InlineData("IQ", true)]
    [InlineData("RO", true)]
    [InlineData("XX", false)]  // not a real assignment
    [InlineData("A", false)]   // too short
    [InlineData("ABC", false)] // too long
    [InlineData("I1", false)]  // digit rejected
    [InlineData("", false)]
    [InlineData("  ", false)]
    [InlineData(null, false)]
    public void TryParse_NormalizesAndValidates(
        string? raw,
        bool expected)
    {
        bool ok = DirectCountryCode.TryParse(raw, out _);

        Assert.Equal(expected, ok);
    }

    [Fact]
    public void TryParse_Invalid_DoesNotReturnResult()
    {
        bool ok = DirectCountryCode.TryParse("XX", out var result);

        Assert.False(ok);
        Assert.Null(result);
    }

    [Fact]
    public void Parse_Invalid_ThrowsCountryCodeFormatException()
    {
        Assert.Throws<CountryCodeFormatException>(
            () => DirectCountryCode.Parse("XX"));
    }

    [Fact]
    public void Code_IsUppercaseCanonical()
    {
        DirectCountryCode code = DirectCountryCode.Parse("iq");

        Assert.Equal("IQ", code.Code);
        Assert.Equal("IQ", code.ToString());
    }

    [Fact]
    public void AllSupported_ContainsIsoCatalogAndIsOrdered()
    {
        IReadOnlyList<DirectCountryCode> all =
            DirectCountryCode.AllSupported;

        // Not a hardcoded 3-entry allow-list.
        Assert.True(all.Count > 200, $"got {all.Count}");
        Assert.Contains(DirectCountryCode.Parse("IR"), all);
        Assert.Contains(DirectCountryCode.Parse("IQ"), all);
        Assert.Contains(DirectCountryCode.Parse("RO"), all);

        // Deterministic ordinal order.
        for (int i = 1; i < all.Count; i++)
        {
            Assert.True(string.CompareOrdinal(
                all[i - 1].Code,
                all[i].Code) < 0);
        }
    }

    [Fact]
    public void DisplayName_PresentationOnly_ForKnownCodes()
    {
        Assert.Equal("Iran", DirectCountryCode.Parse("IR").DisplayName);
        Assert.Equal("Iraq", DirectCountryCode.Parse("IQ").DisplayName);
        Assert.Equal(
            "Romania", DirectCountryCode.Parse("RO").DisplayName);
    }

    [Fact]
    public void DisplayName_FallsBackToCode_WhenNotMapped()
    {
        // A valid ISO code absent from the display-name map still validates
        // and is supported; identity is the code, name is advisory only.
        DirectCountryCode code = DirectCountryCode.Parse("DE");

        Assert.Equal("DE", code.DisplayName);
        Assert.Contains(code, DirectCountryCode.AllSupported);
    }
}
