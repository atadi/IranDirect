using IranDirect.Core.CustomRoutes;

namespace IranDirect.Core.Tests.CustomRoutes;

public sealed class CustomRouteEntryValidatorTests
{
    private readonly CustomRouteEntryValidator _validator = new();

    [Theory]
    [InlineData("example.com", "example.com")]
    [InlineData("Example.COM", "example.com")]
    [InlineData("EXAMPLE.com", "example.com")]
    [InlineData("example.com.", "example.com")]
    [InlineData("  sub.example.com ", "sub.example.com")]
    [InlineData("localhost", "localhost")]
    [InlineData("sub.domain.example.com", "sub.domain.example.com")]
    [InlineData("a-b.example.com", "a-b.example.com")]
    [InlineData("xn--mnchen-3ya.de", "xn--mnchen-3ya.de")]
    public void Domain_Valid_IsNormalized(
        string input,
        string expected)
    {
        CustomRouteValidationResult result =
            _validator.ValidateAndNormalize(
                CustomRouteEntryType.Domain,
                input);

        Assert.True(result.IsValid);
        Assert.Equal(expected, result.NormalizedValue);
    }

    [Theory]
    [InlineData("münchen.de", "xn--mnchen-3ya.de")]
    [InlineData("bücher.example", "xn--bcher-kva.example")]
    public void Domain_ValidIdn_IsPunycodeNormalized(
        string input,
        string expected)
    {
        CustomRouteValidationResult result =
            _validator.ValidateAndNormalize(
                CustomRouteEntryType.Domain,
                input);

        Assert.True(result.IsValid);
        Assert.Equal(expected, result.NormalizedValue);
    }

    [Theory]
    [InlineData("http://example.com")]
    [InlineData("https://example.com/path")]
    [InlineData("example.com/path")]
    [InlineData("example.com\\path")]
    [InlineData("example.com?x=1")]
    [InlineData("example.com#fragment")]
    [InlineData("example.com:8080")]
    [InlineData("user@example.com")]
    [InlineData("*.example.com")]
    [InlineData("foo*bar.com")]
    [InlineData("exa mple.com")]
    [InlineData("a..b.com")]
    [InlineData(".example.com")]
    [InlineData("example..com")]
    [InlineData("-example.com")]
    [InlineData("example-.com")]
    [InlineData("a_b.com")]
    [InlineData("127.0.0.1")]
    [InlineData("8.8.8.8")]
    [InlineData("::1")]
    [InlineData("2001:db8::1")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(".")]
    public void Domain_Invalid_IsRejected(string input)
    {
        CustomRouteValidationResult result =
            _validator.ValidateAndNormalize(
                CustomRouteEntryType.Domain,
                input);

        Assert.False(result.IsValid);
        Assert.Null(result.NormalizedValue);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void Domain_OverlongLabel_IsRejected()
    {
        string input = new string('a', 64) + ".com";

        CustomRouteValidationResult result =
            _validator.ValidateAndNormalize(
                CustomRouteEntryType.Domain,
                input);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Domain_Over255Characters_IsRejected()
    {
        string input =
            string.Join(".", Enumerable.Repeat("abcde", 52));

        CustomRouteValidationResult result =
            _validator.ValidateAndNormalize(
                CustomRouteEntryType.Domain,
                input);

        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData("8.8.8.8", "8.8.8.8")]
    [InlineData("1.2.3.4", "1.2.3.4")]
    [InlineData(" 10.0.0.1 ", "10.0.0.1")]
    [InlineData("192.168.1.255", "192.168.1.255")]
    public void IpAddress_Valid_IsNormalized(
        string input,
        string expected)
    {
        CustomRouteValidationResult result =
            _validator.ValidateAndNormalize(
                CustomRouteEntryType.IpAddress,
                input);

        Assert.True(result.IsValid);
        Assert.Equal(expected, result.NormalizedValue);
    }

    [Theory]
    [InlineData("008.008.008.008")]
    [InlineData("8.8.8.8.8")]
    [InlineData("8.8.8")]
    [InlineData("8.8")]
    [InlineData("8")]
    [InlineData("256.256.256.256")]
    [InlineData("8.8.8.8.")]
    [InlineData(".8.8.8.8")]
    [InlineData("1.2.3.04")]
    [InlineData("0.0.0.0")]
    [InlineData("0.0.0.1")]
    [InlineData("127.0.0.1")]
    [InlineData("127.255.255.254")]
    [InlineData("224.0.0.1")]
    [InlineData("239.255.255.255")]
    [InlineData("255.255.255.255")]
    [InlineData("::1")]
    [InlineData("2001:db8::1")]
    [InlineData("2001:db8:85a3::8a2e:370:7334")]
    [InlineData("abc")]
    [InlineData("1.2.3.x")]
    [InlineData("")]
    [InlineData("   ")]
    public void IpAddress_Invalid_IsRejected(string input)
    {
        CustomRouteValidationResult result =
            _validator.ValidateAndNormalize(
                CustomRouteEntryType.IpAddress,
                input);

        Assert.False(result.IsValid);
        Assert.Null(result.NormalizedValue);
    }

    [Theory]
    [InlineData("10.0.0.1/24", "10.0.0.0/24")]
    [InlineData("10.0.0.0/24", "10.0.0.0/24")]
    [InlineData("192.168.1.5/32", "192.168.1.5/32")]
    [InlineData("172.16.0.1/16", "172.16.0.0/16")]
    [InlineData("10.1.2.3/8", "10.0.0.0/8")]
    [InlineData("8.8.8.8/30", "8.8.8.8/30")]
    [InlineData(" 203.0.113.77/24 ", "203.0.113.0/24")]
    public void Cidr_Valid_HostBitsAreNormalized(
        string input,
        string expected)
    {
        CustomRouteValidationResult result =
            _validator.ValidateAndNormalize(
                CustomRouteEntryType.Cidr,
                input);

        Assert.True(result.IsValid);
        Assert.Equal(expected, result.NormalizedValue);
    }

    [Theory]
    [InlineData("10.0.0.0/0")]
    [InlineData("0.0.0.0/0")]
    [InlineData("10.0.0.0/33")]
    [InlineData("10.0.0.0/-1")]
    [InlineData("10.0.0.0")]
    [InlineData("10.0.0.0/")]
    [InlineData("/24")]
    [InlineData("10.0.0.0/24/2")]
    [InlineData("10.0.0.0/24.5")]
    [InlineData("10.0.0.0/x")]
    [InlineData("10.0.0.256/24")]
    [InlineData("010.0.0.0/24")]
    [InlineData("10.0.0.0.1/24")]
    [InlineData("::1/128")]
    [InlineData("2001:db8::/64")]
    [InlineData("fe80::/10")]
    [InlineData("127.0.0.0/8")]
    [InlineData("127.0.0.1/32")]
    [InlineData("224.0.0.0/4")]
    [InlineData("239.255.255.255/32")]
    [InlineData("0.0.0.0/8")]
    [InlineData("0.0.0.1/32")]
    [InlineData("255.255.255.255/32")]
    [InlineData("255.0.0.0/8")]
    [InlineData("")]
    [InlineData("   ")]
    public void Cidr_Invalid_IsRejected(string input)
    {
        CustomRouteValidationResult result =
            _validator.ValidateAndNormalize(
                CustomRouteEntryType.Cidr,
                input);

        Assert.False(result.IsValid);
        Assert.Null(result.NormalizedValue);
    }

    [Fact]
    public void UnknownType_IsRejected()
    {
        CustomRouteValidationResult result =
            _validator.ValidateAndNormalize(
                (CustomRouteEntryType)999,
                "example.com");

        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
    }
}
