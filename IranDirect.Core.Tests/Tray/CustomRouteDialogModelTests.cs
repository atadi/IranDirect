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
    public void MapRows_MapsDomainRowWithCacheDetails()
    {
        Guid id = Guid.NewGuid();
        DateTimeOffset stamp =
            new(2026, 1, 2, 0, 0, 0, TimeSpan.Zero);

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
            ],
            [
                new CustomRouteDnsCacheStatus
                {
                    CustomRouteEntryId = id,
                    Domain = "example.com",
                    Enabled = true,
                    State = CustomRouteDnsCacheState.Fresh,
                    IPv4Addresses = ["8.8.8.8", "9.9.9.9"],
                    ExpiresAt = stamp
                }
            ]);

        CustomRouteListRow row = Assert.Single(rows);
        Assert.Equal(id, row.Id);
        Assert.True(row.Enabled);
        Assert.Equal(CustomRouteEntryType.Domain, row.Type);
        Assert.Equal("example.com", row.Value);
        Assert.Equal("site", row.Description);
        Assert.Equal("Fresh", row.CacheState);
        Assert.Equal("8.8.8.8, 9.9.9.9", row.Addresses);
        Assert.Matches(
            @"\d{4}-\d{2}-\d{2} \d{2}:\d{2}",
            row.Expires);
    }

    [Fact]
    public void MapRows_WhenNoMatchingStatus_ShowsPlaceholders()
    {
        IReadOnlyList<CustomRouteListRow> rows =
            CustomRouteDialogModel.MapRows(
            [
                new CustomRouteEntry
                {
                    Id = Guid.NewGuid(),
                    Type = CustomRouteEntryType.Domain,
                    Value = "example.com",
                    Enabled = true
                }
            ],
            []);

        CustomRouteListRow row = Assert.Single(rows);
        Assert.Equal("-", row.CacheState);
        Assert.Equal("-", row.Addresses);
        Assert.Equal("-", row.Expires);
    }

    [Theory]
    [InlineData(CustomRouteEntryType.IpAddress)]
    [InlineData(CustomRouteEntryType.Cidr)]
    public void MapRows_NonDomainRows_ShowPlaceholders(
        CustomRouteEntryType type)
    {
        IReadOnlyList<CustomRouteListRow> rows =
            CustomRouteDialogModel.MapRows(
            [
                new CustomRouteEntry
                {
                    Id = Guid.NewGuid(),
                    Type = type,
                    Value = "8.8.8.8",
                    Enabled = true
                }
            ],
            []);

        CustomRouteListRow row = Assert.Single(rows);
        Assert.Equal("-", row.CacheState);
        Assert.Equal("-", row.Addresses);
        Assert.Equal("-", row.Expires);
    }

    [Theory]
    [InlineData(CustomRouteEntryType.Domain, true)]
    [InlineData(CustomRouteEntryType.IpAddress, false)]
    [InlineData(CustomRouteEntryType.Cidr, false)]
    public void CanInvalidateCache_DomainOnly(
        CustomRouteEntryType type,
        bool expected)
    {
        CustomRouteListRow row = new(
            Guid.NewGuid(),
            Enabled: true,
            type,
            "value",
            Description: null,
            CacheState: "-",
            Addresses: "-",
            Expires: "-");

        Assert.Equal(
            expected,
            CustomRouteDialogModel.CanInvalidateCache(row));
    }

    [Fact]
    public void CanInvalidateCache_NullRow_ReturnsFalse()
    {
        Assert.False(
            CustomRouteDialogModel.CanInvalidateCache(null));
    }

    [Theory]
    [InlineData(
        true,
        IranDirectCommand.CustomRoutesInvalidateAllCaches)]
    [InlineData(
        false,
        IranDirectCommand.CustomRoutesInvalidateCache)]
    public void GetInvalidateCommand_MapsTarget(
        bool all,
        IranDirectCommand expected)
    {
        Assert.Equal(
            expected,
            CustomRouteDialogModel.GetInvalidateCommand(all));
    }

    [Fact]
    public void FormatCacheAddresses_JoinsAndFallsBack()
    {
        Assert.Equal(
            "8.8.8.8, 9.9.9.9",
            CustomRouteDialogModel.FormatCacheAddresses(
                ["8.8.8.8", "9.9.9.9"]));
        Assert.Equal(
            "-",
            CustomRouteDialogModel.FormatCacheAddresses([]));
        Assert.Equal(
            "-",
            CustomRouteDialogModel.FormatCacheAddresses(null));
    }

    [Fact]
    public void FormatCacheTimestamp_FormatsOrFallsBack()
    {
        Assert.Equal(
            "-",
            CustomRouteDialogModel.FormatCacheTimestamp(null));
        Assert.Matches(
            @"\d{4}-\d{2}-\d{2} \d{2}:\d{2}",
            CustomRouteDialogModel.FormatCacheTimestamp(
                new DateTimeOffset(
                    2026, 1, 1, 0, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void InvalidateAllConfirmationMessage_IsPresent()
    {
        Assert.False(
            string.IsNullOrWhiteSpace(
                CustomRouteDialogModel
                    .InvalidateAllConfirmationMessage));
        Assert.Contains(
            "cache",
            CustomRouteDialogModel.InvalidateAllConfirmationMessage,
            StringComparison.OrdinalIgnoreCase);
    }
}
