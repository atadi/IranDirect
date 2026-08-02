using IranDirect.Cli;

namespace IranDirect.Core.Tests.Cli;

public sealed class SupportBundleCliParserTests
{
    [Fact]
    public void Parse_NoArgs_IsValid_NoPath()
    {
        SupportBundleCliParseResult result =
            SupportBundleCliParser.Parse([]);

        Assert.True(result.IsValid);
        Assert.Null(result.OutputPath);
    }

    [Fact]
    public void Parse_OneArg_IsValid_PathEchoed()
    {
        SupportBundleCliParseResult result =
            SupportBundleCliParser.Parse(["C:\\my.zip"]);

        Assert.True(result.IsValid);
        Assert.Equal("C:\\my.zip", result.OutputPath);
    }

    [Fact]
    public void Parse_TwoArgs_Invalid()
    {
        SupportBundleCliParseResult result =
            SupportBundleCliParser.Parse(
                ["a.zip", "b.zip"]);

        Assert.False(result.IsValid);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void Parse_EmptyArg_Invalid()
    {
        SupportBundleCliParseResult result =
            SupportBundleCliParser.Parse([""]);

        Assert.False(result.IsValid);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void Parse_NullArgs_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => SupportBundleCliParser.Parse(null!));
    }
}
