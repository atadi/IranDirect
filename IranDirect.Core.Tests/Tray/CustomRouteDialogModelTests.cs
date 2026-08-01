using IranDirect.Core.CustomRoutes;
using IranDirect.Core.Ipc;
using IranDirect.Tray;

namespace IranDirect.Core.Tests.Tray;

public sealed class CustomRouteDialogModelTests
{
    [Theory]
    [InlineData(
        CustomRouteEntryType.Domain,
        CustomRouteDialogModel.DomainLabel)]
    [InlineData(
        CustomRouteEntryType.IpAddress,
        CustomRouteDialogModel.IpAddressLabel)]
    [InlineData(
        CustomRouteEntryType.Cidr,
        CustomRouteDialogModel.CidrLabel)]
    public void GetTypeLabel_MapsEachType(
        CustomRouteEntryType type,
        string expected)
    {
        Assert.Equal(expected, CustomRouteDialogModel.GetTypeLabel(type));
    }

    [Fact]
    public void TryGetType_RoundTripsAllLabels()
    {
        foreach (string label in CustomRouteDialogModel.TypeLabels)
        {
            bool found =
                CustomRouteDialogModel.TryGetType(
                    label,
                    out CustomRouteEntryType type);

            Assert.True(found);
            Assert.Equal(
                label,
                CustomRouteDialogModel.GetTypeLabel(type));
        }
    }

    [Fact]
    public void TryGetType_IsCaseInsensitive()
    {
        Assert.True(
            CustomRouteDialogModel.TryGetType(
                "domain",
                out CustomRouteEntryType type));
        Assert.Equal(CustomRouteEntryType.Domain, type);
    }

    [Fact]
    public void TryGetType_UnknownLabel_ReturnsFalse()
    {
        Assert.False(
            CustomRouteDialogModel.TryGetType(
                "wildcard",
                out _));
    }

    [Theory]
    [InlineData(
        CustomRouteEntryType.Domain,
        IranDirectCommand.CustomRoutesAddDomain)]
    [InlineData(
        CustomRouteEntryType.IpAddress,
        IranDirectCommand.CustomRoutesAddIp)]
    [InlineData(
        CustomRouteEntryType.Cidr,
        IranDirectCommand.CustomRoutesAddCidr)]
    public void GetAddCommand_MapsEachType(
        CustomRouteEntryType type,
        IranDirectCommand expected)
    {
        Assert.Equal(
            expected,
            CustomRouteDialogModel.GetAddCommand(type));
    }

    [Fact]
    public void MapRows_MapsEntriesToRows()
    {
        Guid id = Guid.NewGuid();

        IReadOnlyList<CustomRouteListRow> rows =
            CustomRouteDialogModel.MapRows(
            [
                new CustomRouteEntry
                {
                    Id = id,
                    Type = CustomRouteEntryType.Domain,
                    Value = "example.com",
                    Enabled = true,
                    Description = "site"
                }
            ]);

        CustomRouteListRow row = Assert.Single(rows);
        Assert.Equal(id, row.Id);
        Assert.True(row.Enabled);
        Assert.Equal(CustomRouteEntryType.Domain, row.Type);
        Assert.Equal("example.com", row.Value);
        Assert.Equal("site", row.Description);
    }

    [Fact]
    public void BuildResolutionSummary_IncludesFailures()
    {
        CustomRouteResolutionResult result = new()
        {
            Prefixes = ["8.8.8.8/32"],
            Failures =
            [
                new CustomRouteResolutionFailure
                {
                    Type = CustomRouteEntryType.Domain,
                    Value = "broken.example",
                    Reason = "NXDOMAIN"
                }
            ]
        };

        string summary =
            CustomRouteDialogModel.BuildResolutionSummary(result);

        Assert.Contains("Resolved 1 prefix(es).", summary);
        Assert.Contains("1 failure(s).", summary);
    }

    [Fact]
    public void BuildResolutionSummary_WhenNoFailures_OmitsFailures()
    {
        CustomRouteResolutionResult result = new()
        {
            Prefixes = [],
            Failures = []
        };

        string summary =
            CustomRouteDialogModel.BuildResolutionSummary(result);

        Assert.Contains("Resolved 0 prefix(es).", summary);
        Assert.DoesNotContain("failure", summary);
    }
}
