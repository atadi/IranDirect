using PathVeer.Core.Cli;
using PathVeer.Core.CustomRoutes;

namespace PathVeer.Core.Tests.Cli;

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

    [Fact]
    public void RenderCacheStatus_WhenEmpty_ShowsNoDomainsMessage()
    {
        string[] lines = CustomRouteCliRenderer
            .RenderCacheStatus([])
            .ToArray();

        Assert.Contains("No domains configured.", lines);
    }

    [Fact]
    public void RenderCacheStatus_IncludesAllColumns()
    {
        Guid id = Guid.NewGuid();
        DateTimeOffset stamp = new(
            2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        string[] lines = CustomRouteCliRenderer
            .RenderCacheStatus(
            [
                new CustomRouteDnsCacheStatus
                {
                    CustomRouteEntryId = id,
                    Domain = "example.com",
                    Enabled = true,
                    State = CustomRouteDnsCacheState.Fresh,
                    IPv4Addresses = ["8.8.8.8", "9.9.9.9"],
                    LastSucceededAt = stamp,
                    ExpiresAt = stamp,
                    StaleUntil = stamp
                }
            ])
            .ToArray();

        string header = lines[1];
        Assert.Contains("Domain", header);
        Assert.Contains("Enabled", header);
        Assert.Contains("State", header);
        Assert.Contains("Addresses", header);
        Assert.Contains("Last Success", header);
        Assert.Contains("Expires", header);
        Assert.Contains("Stale Until", header);
        Assert.Contains("Last Error", header);

        string row = lines[2];
        Assert.Contains("example.com", row);
        Assert.Contains("yes", row);
        Assert.Contains("Fresh", row);
        Assert.Contains("8.8.8.8, 9.9.9.9", row);
        Assert.Contains("2026-01-01 00:00:00", row);
    }

    [Fact]
    public void RenderCacheStatus_MissingFields_ShowsPlaceholders()
    {
        string[] lines = CustomRouteCliRenderer
            .RenderCacheStatus(
            [
                new CustomRouteDnsCacheStatus
                {
                    Domain = "example.com",
                    Enabled = false,
                    State = CustomRouteDnsCacheState.Missing,
                    IPv4Addresses = []
                }
            ])
            .ToArray();

        string row = lines[2];
        Assert.Contains("no", row);
        Assert.Contains("Missing", row);
        Assert.DoesNotContain("2026", row);
    }
}
