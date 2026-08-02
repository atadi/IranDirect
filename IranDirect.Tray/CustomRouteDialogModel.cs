using IranDirect.Core.CustomRoutes;
using IranDirect.Core.Ipc;

namespace IranDirect.Tray;

public sealed record CustomRouteListRow(
    Guid Id,
    bool Enabled,
    CustomRouteEntryType Type,
    string Value,
    string? Description,
    string CacheState,
    string Addresses,
    string Expires);

public static class CustomRouteDialogModel
{
    public const string DomainLabel = "Domain";
    public const string IpAddressLabel = "IP Address";
    public const string CidrLabel = "CIDR";

    public const string NotAvailable = "-";

    public const string InvalidateAllConfirmationMessage =
        "Invalidate all DNS cache records?";

    public static string GetTypeLabel(
        CustomRouteEntryType type)
    {
        return type switch
        {
            CustomRouteEntryType.Domain => DomainLabel,
            CustomRouteEntryType.IpAddress => IpAddressLabel,
            CustomRouteEntryType.Cidr => CidrLabel,
            _ => type.ToString()
        };
    }

    public static bool TryGetType(
        string label,
        out CustomRouteEntryType type)
    {
        if (string.Equals(
                label,
                DomainLabel,
                StringComparison.OrdinalIgnoreCase))
        {
            type = CustomRouteEntryType.Domain;
            return true;
        }

        if (string.Equals(
                label,
                IpAddressLabel,
                StringComparison.OrdinalIgnoreCase))
        {
            type = CustomRouteEntryType.IpAddress;
            return true;
        }

        if (string.Equals(
                label,
                CidrLabel,
                StringComparison.OrdinalIgnoreCase))
        {
            type = CustomRouteEntryType.Cidr;
            return true;
        }

        type = default;
        return false;
    }

    public static IReadOnlyList<string> TypeLabels =>
    [
        DomainLabel,
        IpAddressLabel,
        CidrLabel
    ];

    public static IranDirectCommand GetAddCommand(
        CustomRouteEntryType type)
    {
        return type switch
        {
            CustomRouteEntryType.Domain =>
                IranDirectCommand.CustomRoutesAddDomain,
            CustomRouteEntryType.IpAddress =>
                IranDirectCommand.CustomRoutesAddIp,
            CustomRouteEntryType.Cidr =>
                IranDirectCommand.CustomRoutesAddCidr,
            _ => throw new ArgumentOutOfRangeException(
                nameof(type))
        };
    }

    public static IReadOnlyList<CustomRouteListRow> MapRows(
        IReadOnlyList<CustomRouteEntry> entries,
        IReadOnlyList<CustomRouteDnsCacheStatus> statuses)
    {
        Dictionary<Guid, CustomRouteDnsCacheStatus> byId =
            statuses.ToDictionary(
                status => status.CustomRouteEntryId);

        return entries
            .Select(entry =>
            {
                bool isDomain =
                    entry.Type == CustomRouteEntryType.Domain;

                CustomRouteDnsCacheStatus? status =
                    isDomain
                        ? byId.GetValueOrDefault(entry.Id)
                        : null;

                return new CustomRouteListRow(
                    entry.Id,
                    entry.Enabled,
                    entry.Type,
                    entry.Value,
                    entry.Description,
                    isDomain
                        ? status?.State.ToString()
                            ?? NotAvailable
                        : NotAvailable,
                    isDomain
                        ? FormatCacheAddresses(
                            status?.IPv4Addresses)
                        : NotAvailable,
                    isDomain
                        ? FormatCacheTimestamp(
                            status?.ExpiresAt)
                        : NotAvailable);
            })
            .ToArray();
    }

    public static bool CanInvalidateCache(
        CustomRouteListRow? row) =>
        row is not null &&
        row.Type == CustomRouteEntryType.Domain;

    public static IranDirectCommand GetInvalidateCommand(
        bool all)
    {
        return all
            ? IranDirectCommand.CustomRoutesInvalidateAllCaches
            : IranDirectCommand.CustomRoutesInvalidateCache;
    }

    public static string FormatCacheAddresses(
        IReadOnlyList<string>? addresses) =>
        addresses is null || addresses.Count == 0
            ? NotAvailable
            : string.Join(", ", addresses);

    public static string FormatCacheTimestamp(
        DateTimeOffset? timestamp) =>
        timestamp is null
            ? NotAvailable
            : timestamp.Value.ToLocalTime()
                .ToString("yyyy-MM-dd HH:mm");
}
