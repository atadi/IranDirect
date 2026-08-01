using System.Globalization;
using System.Net;

namespace IranDirect.Core.CustomRoutes;

public sealed class CustomRouteEntryValidator
{
    private const int MaxDomainLength = 253;
    private const int MaxLabelLength = 63;
    private const int MinPrefixLength = 1;
    private const int MaxPrefixLength = 32;

    public CustomRouteValidationResult ValidateAndNormalize(
        CustomRouteEntryType type,
        string? value)
    {
        return type switch
        {
            CustomRouteEntryType.Domain =>
                ValidateDomain(value),
            CustomRouteEntryType.IpAddress =>
                ValidateIpAddress(value),
            CustomRouteEntryType.Cidr =>
                ValidateCidr(value),
            _ => Invalid(
                $"Unsupported custom route entry type: {type}.")
        };
    }

    private static CustomRouteValidationResult ValidateDomain(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Invalid(
                "Domain must not be empty.");
        }

        string s = value.Trim();

        if (s.Contains("://", StringComparison.Ordinal))
        {
            return Invalid(
                "Domain must not contain a scheme (for example https://).");
        }

        if (s.Contains('*'))
        {
            return Invalid(
                "Domain must not contain a wildcard.");
        }

        foreach (char c in s)
        {
            if (char.IsWhiteSpace(c)
                || c is '/' or '\\' or '?' or '#' or ':' or '@')
            {
                return Invalid(
                    $"Domain contains the invalid character '{c}'.");
            }
        }

        s = s.TrimEnd('.');

        if (s.Length == 0)
        {
            return Invalid(
                "Domain must not be empty.");
        }

        string ascii;

        try
        {
            ascii = new IdnMapping().GetAscii(s);
        }
        catch (ArgumentException)
        {
            return Invalid(
                "Domain contains an invalid label.");
        }

        ascii = ascii.ToLowerInvariant();

        if (ascii.Length > MaxDomainLength)
        {
            return Invalid(
                "Domain is too long.");
        }

        foreach (string label in ascii.Split('.'))
        {
            if (label.Length == 0
                || label.Length > MaxLabelLength)
            {
                return Invalid(
                    "Domain contains an invalid label length.");
            }

            if (label[0] == '-' || label[^1] == '-')
            {
                return Invalid(
                    "Domain labels must not start or end with a hyphen.");
            }

            foreach (char c in label)
            {
                if (c is not (>= 'a' and <= 'z')
                    and not (>= '0' and <= '9')
                    and not '-')
                {
                    return Invalid(
                        $"Domain label contains the invalid character '{c}'.");
                }
            }
        }

        if (IPAddress.TryParse(ascii, out _))
        {
            return Invalid(
                "Domain must not be an IP literal.");
        }

        return Valid(ascii);
    }

    private static CustomRouteValidationResult ValidateIpAddress(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Invalid(
                "IPv4 address must not be empty.");
        }

        string s = value.Trim();

        if (!TryParseIpv4(s, out byte[] bytes))
        {
            return Invalid(
                "IPv4 address is not a valid dotted-quad address.");
        }

        if (IsUnsafeNetwork(bytes))
        {
            return Invalid(
                "IPv4 address is in a loopback, multicast, " +
                "unspecified, or broadcast network.");
        }

        return Valid(FormatIpv4(bytes));
    }

    private static CustomRouteValidationResult ValidateCidr(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Invalid(
                "CIDR must not be empty.");
        }

        string s = value.Trim();

        string[] parts = s.Split('/');

        if (parts.Length != 2)
        {
            return Invalid(
                "CIDR must be in the form <ipv4-address>/<prefix-length>.");
        }

        string ipPart = parts[0].Trim();
        string prefixPart = parts[1].Trim();

        if (!TryParseIpv4(ipPart, out byte[] ipBytes))
        {
            return Invalid(
                "CIDR address must be a valid IPv4 dotted-quad address.");
        }

        if (!int.TryParse(prefixPart, out int prefixLength)
            || prefixLength is < MinPrefixLength or > MaxPrefixLength)
        {
            return Invalid(
                $"CIDR prefix length must be an integer " +
                $"between {MinPrefixLength} and {MaxPrefixLength}.");
        }

        byte[] network = ApplyMask(ipBytes, prefixLength);

        if (IsUnsafeNetwork(network))
        {
            return Invalid(
                "CIDR range is in a loopback, multicast, " +
                "unspecified, or broadcast network.");
        }

        return Valid(
            $"{FormatIpv4(network)}/{prefixLength}");
    }

    private static bool TryParseIpv4(
        string s,
        out byte[] bytes)
    {
        bytes = [];

        if (string.IsNullOrEmpty(s))
        {
            return false;
        }

        string[] parts = s.Split('.');

        if (parts.Length != 4)
        {
            return false;
        }

        byte[] result = new byte[4];

        for (int i = 0; i < 4; i++)
        {
            string part = parts[i];

            if (part.Length == 0
                || part.Length > 3
                || (part.Length > 1 && part[0] == '0'))
            {
                return false;
            }

            foreach (char c in part)
            {
                if (c is not (>= '0' and <= '9'))
                {
                    return false;
                }
            }

            if (!byte.TryParse(part, out byte octet))
            {
                return false;
            }

            result[i] = octet;
        }

        bytes = result;
        return true;
    }

    private static byte[] ApplyMask(
        byte[] address,
        int prefixLength)
    {
        byte[] result = new byte[4];

        for (int i = 0; i < 4; i++)
        {
            int remaining = prefixLength - i * 8;

            result[i] = remaining >= 8
                ? address[i]
                : remaining <= 0
                    ? (byte)0
                    : (byte)(address[i] & (0xFF << (8 - remaining)));
        }

        return result;
    }

    private static bool IsUnsafeNetwork(byte[] address) =>
        address[0] == 0
        || address[0] == 127
        || address[0] is >= 224 and <= 239
        || address[0] == 255;

    private static string FormatIpv4(byte[] address) =>
        $"{address[0]}.{address[1]}.{address[2]}.{address[3]}";

    private static CustomRouteValidationResult Valid(
        string normalizedValue) =>
        new() { NormalizedValue = normalizedValue };

    private static CustomRouteValidationResult Invalid(
        string error) =>
        new() { Errors = [error] };
}
