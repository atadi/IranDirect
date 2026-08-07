using IranDirect.Core.Configuration;

namespace IranDirect.Core.Tests.Configuration;

public sealed class DirectCountryCodeTests
{
    [Theory]
    [InlineData("IR", "IR")]
    [InlineData("ir", "IR")]
    [InlineData("Ir", "IR")]
    [InlineData("IQ", "IQ")]
    [InlineData("iq", "IQ")]
    [InlineData("RO", "RO")]
    [InlineData("ro", "RO")]
    [InlineData("US", "US")]
    [InlineData("DE", "DE")]
    public void TryParse_NormalizesToUppercaseInvariant(
        string raw,
        string expected)
    {
        Assert.True(
            DirectCountryCode.TryParse(raw, out DirectCountryCode? result));
        Assert.Equal(expected, result!.Code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("I")]
    [InlineData("IRQ")]
    [InlineData("1R")]
    [InlineData("R1")]
    [InlineData("XX")]
    [InlineData("UK")]   // United Kingdom is GB in ISO 3166-1
    [InlineData("ir ")]  // trailing space
    [InlineData(" ir")]  // leading space
    public void TryParse_RejectsInvalid(string? raw)
    {
        Assert.False(
            DirectCountryCode.TryParse(raw, out _));
    }

    [Fact]
    public void Parse_ValidRoundTrips()
    {
        DirectCountryCode code = DirectCountryCode.Parse("ro");
        Assert.Equal("RO", code.Code);
        Assert.Equal("RO", code.ToString());
    }

    [Fact]
    public void Parse_Invalid_ThrowsCountryCodeFormatException()
    {
        Assert.Throws<CountryCodeFormatException>(
            () => DirectCountryCode.Parse("IRQ"));
    }

    [Fact]
    public void Equality_IsOrdinalAndCaseInsensitiveToInput()
    {
        DirectCountryCode a = DirectCountryCode.Parse("IR");
        DirectCountryCode b = DirectCountryCode.Parse("ir");

        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.False(a != b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void IR_IsTheLegacyDefault()
    {
        Assert.True(
            DirectCountryRouting.IsDirectCountrySupported(null));
        Assert.True(
            DirectCountryRouting.IsDirectCountrySupported(
                DirectCountryCode.IR));
    }

    [Theory]
    [InlineData("IQ")]
    [InlineData("RO")]
    [InlineData("DE")]
    [InlineData("US")]
    public void NonIR_IsNotYetSupportedUntilSourceGeneralized(
        string code)
    {
        Assert.False(
            DirectCountryRouting.IsDirectCountrySupported(
                DirectCountryCode.Parse(code)));
    }
}
