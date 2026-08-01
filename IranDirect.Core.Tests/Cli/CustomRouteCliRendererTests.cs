using IranDirect.Core.Cli;
using IranDirect.Core.CustomRoutes;

namespace IranDirect.Core.Tests.Cli;

public sealed class CustomRouteCliRendererTests
{
    [Fact]
    public void RenderList_WhenEmpty_ShowsNoRoutesMessage()
    {
        string[] lines = CustomRouteCliRenderer
            .RenderList([])
            .ToArray();

        Assert.Contains(
            "No custom routes configured.",
            lines);
    }

    [Fact]
    public void RenderList_IncludesAllColumns()
    {
        Guid id = Guid.NewGuid();

        string[] lines = CustomRouteCliRenderer
            .RenderList(
            [
                new CustomRouteEntry
                {
                    Id = id,
                    Type = CustomRouteEntryType.Domain,
                    Value = "example.com",
                    Enabled = true,
                    Description = "my site"
                }
            ])
            .ToArray();

        string header = lines[1];
        Assert.Contains("ID", header);
        Assert.Contains("Type", header);
        Assert.Contains("Enabled", header);
        Assert.Contains("Value", header);
        Assert.Contains("Description", header);

        string row = lines[2];
        Assert.Contains(id.ToString(), row);
        Assert.Contains("Domain", row);
        Assert.Contains("example.com", row);
        Assert.Contains("yes", row);
        Assert.Contains("my site", row);
    }

    [Fact]
    public void RenderResolve_IncludesCountPrefixesAndFailures()
    {
        string[] lines = CustomRouteCliRenderer
            .RenderResolve(
                new CustomRouteResolutionResult
                {
                    Prefixes = ["8.8.8.8/32", "10.0.0.0/24"],
                    Failures =
                    [
                        new CustomRouteResolutionFailure
                        {
                            Type = CustomRouteEntryType.Domain,
                            Value = "broken.example",
                            Reason = "NXDOMAIN"
                        }
                    ]
                })
            .ToArray();

        Assert.Contains(
            "Resolved prefixes: 2",
            lines);
        Assert.Contains("8.8.8.8/32", lines);
        Assert.Contains("10.0.0.0/24", lines);
        Assert.Contains("Failures: 1", lines);
        Assert.Contains(
            "- [Domain] broken.example: NXDOMAIN",
            lines);
    }

    [Fact]
    public void RenderResolve_WhenNoFailures_ShowsZero()
    {
        string[] lines = CustomRouteCliRenderer
            .RenderResolve(
                new CustomRouteResolutionResult
                {
                    Prefixes = [],
                    Failures = []
                })
            .ToArray();

        Assert.Contains("Resolved prefixes: 0", lines);
        Assert.Contains("Failures: 0", lines);
    }
}
