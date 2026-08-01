using IranDirect.Core.CustomRoutes;
using IranDirect.Core.Ipc;

namespace IranDirect.Tray;

public sealed record CustomRouteListRow(
    Guid Id,
    bool Enabled,
    CustomRouteEntryType Type,
    string Value,
    string? Description);

public static class CustomRouteDialogModel
{
    public const string DomainLabel = "Domain";
    public const string IpAddressLabel = "IP Address";
    public const string CidrLabel = "CIDR";

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
        IReadOnlyList<CustomRouteEntry> entries)
    {
        return entries
            .Select(entry =>
                new CustomRouteListRow(
                    entry.Id,
                    entry.Enabled,
                    entry.Type,
                    entry.Value,
                    entry.Description))
            .ToArray();
    }

    public static string BuildResolutionSummary(
        CustomRouteResolutionResult result)
    {
        string summary =
            $"Resolved {result.Prefixes.Count} prefix(es).";

        if (result.Failures.Count == 0)
        {
            return summary;
        }

        return summary +
               $" {result.Failures.Count} failure(s).";
    }
}
