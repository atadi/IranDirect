using IranDirect.Core.Cli;

namespace IranDirect.Core.Tests.Cli;

public sealed class CustomRouteCliParserTests
{
    [Fact]
    public void Parse_Empty_ReturnsList()
    {
        CustomRouteCliParseResult result =
            CustomRouteCliParser.Parse([]);

        Assert.True(result.IsValid);
        Assert.Equal(
            CustomRouteCliCommand.List,
            result.Command);
    }

    [Fact]
    public void Parse_List_ReturnsList()
    {
        CustomRouteCliParseResult result =
            CustomRouteCliParser.Parse(["list"]);

        Assert.True(result.IsValid);
        Assert.Equal(
            CustomRouteCliCommand.List,
            result.Command);
    }

    [Fact]
    public void Parse_Resolve_ReturnsResolve()
    {
        CustomRouteCliParseResult result =
            CustomRouteCliParser.Parse(["resolve"]);

        Assert.True(result.IsValid);
        Assert.Equal(
            CustomRouteCliCommand.Resolve,
            result.Command);
    }

    [Fact]
    public void Parse_AddDomain_WithValueAndDescription()
    {
        CustomRouteCliParseResult result =
            CustomRouteCliParser.Parse(
                ["add-domain", "Example.COM.", "my site"]);

        Assert.True(result.IsValid);
        Assert.Equal(
            CustomRouteCliCommand.AddDomain,
            result.Command);
        Assert.Equal("Example.COM.", result.Value);
        Assert.Equal("my site", result.Description);
    }

    [Fact]
    public void Parse_AddDomain_WithoutValue_ReturnsError()
    {
        CustomRouteCliParseResult result =
            CustomRouteCliParser.Parse(["add-domain"]);

        Assert.False(result.IsValid);
        Assert.Contains("value is required", result.Error);
    }

    [Fact]
    public void Parse_AddIp_ReturnsAddIp()
    {
        CustomRouteCliParseResult result =
            CustomRouteCliParser.Parse(["add-ip", "8.8.8.8"]);

        Assert.True(result.IsValid);
        Assert.Equal(
            CustomRouteCliCommand.AddIp,
            result.Command);
        Assert.Equal("8.8.8.8", result.Value);
        Assert.Null(result.Description);
    }

    [Fact]
    public void Parse_AddCidr_ReturnsAddCidr()
    {
        CustomRouteCliParseResult result =
            CustomRouteCliParser.Parse(
                ["add-cidr", "10.0.0.0/24"]);

        Assert.True(result.IsValid);
        Assert.Equal(
            CustomRouteCliCommand.AddCidr,
            result.Command);
    }

    [Fact]
    public void Parse_Enable_WithValidGuid_ReturnsEnable()
    {
        Guid id = Guid.NewGuid();

        CustomRouteCliParseResult result =
            CustomRouteCliParser.Parse(["enable", id.ToString()]);

        Assert.True(result.IsValid);
        Assert.Equal(
            CustomRouteCliCommand.Enable,
            result.Command);
        Assert.Equal(id.ToString(), result.Value);
    }

    [Theory]
    [InlineData("enable")]
    [InlineData("enable", "not-a-guid")]
    [InlineData("disable")]
    [InlineData("remove")]
    public void Parse_InvalidId_ReturnsError(
        params string[] args)
    {
        CustomRouteCliParseResult result =
            CustomRouteCliParser.Parse(args);

        Assert.False(result.IsValid);
        Assert.Contains("GUID", result.Error);
    }

    [Fact]
    public void Parse_UnknownSubcommand_ReturnsError()
    {
        CustomRouteCliParseResult result =
            CustomRouteCliParser.Parse(["frobnicate"]);

        Assert.False(result.IsValid);
        Assert.Contains("Unknown", result.Error);
    }
}
