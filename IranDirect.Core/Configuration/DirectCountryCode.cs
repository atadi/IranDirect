using System.Globalization;

namespace IranDirect.Core.Configuration;

/// <summary>
/// Immutable ISO 3166-1 alpha-2 country identity used to select the direct
/// (bypass-VPN) routing dataset.
///
/// Semantics: the destination country whose IP prefixes should use the local
/// route instead of the VPN. It is NOT the client's physical location, the VPN
/// exit country, nationality, locale, language, or timezone.
///
/// Canonical form is two uppercase ASCII letters. Validation requires a
/// recognized ISO 3166-1 alpha-2 assignment (centralized immutable catalog), so
/// country strings never need to be scattered or re-validated across the
/// codebase.
/// </summary>
public sealed class DirectCountryCode :
    IEquatable<DirectCountryCode>
{
    /// <summary>The canonical, normalized ISO alpha-2 code (e.g. "IR").</summary>
    public string Code { get; }

    private DirectCountryCode(string code)
    {
        Code = code;
    }

    /// <summary>Implicit legacy / default country: Iran.</summary>
    public static DirectCountryCode IR { get; } =
        new("IR");

    /// <summary>Attempts to parse a raw country value.</summary>
    /// <returns>
    /// <c>true</c> when <paramref name="raw"/> normalizes to a recognized ISO
    /// alpha-2 code; <paramref name="result"/> holds the canonical value.
    /// <c>false</c> for null, whitespace, wrong length, non-ASCII letters, or
    /// an unrecognized assignment.
    /// </returns>
    public static bool TryParse(
        string? raw,
        out DirectCountryCode? result)
    {
        result = null;

        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        string normalized = Normalize(raw!);

        if (normalized is null)
        {
            return false;
        }

        if (!KnownCodes.Contains(normalized))
        {
            return false;
        }

        result = new DirectCountryCode(normalized);
        return true;
    }

    /// <summary>
    /// Parses a raw country value, throwing
    /// <see cref="CountryCodeFormatException"/> when it is not a recognized
    /// ISO 3166-1 alpha-2 code.
    /// </summary>
    public static DirectCountryCode Parse(string? raw)
    {
        if (!TryParse(raw, out DirectCountryCode? result))
        {
            throw new CountryCodeFormatException(
                $"'{raw}' is not a recognized ISO 3166-1 alpha-2 country code.");
        }

        return result!;
    }

    /// <summary>
    /// Normalizes a candidate to uppercase invariant two-letter ASCII form,
    /// or <c>null</c> if it cannot be a valid code. No culture-sensitive
    /// behavior is involved.
    /// </summary>
    private static string? Normalize(string raw)
    {
        // Reject anything that is not exactly two ASCII letters up front so
        // lookalikes (full-width, accented, digits, punctuation) are refused.
        if (raw.Length != 2)
        {
            return null;
        }

        char c0 = raw[0];
        char c1 = raw[1];

        if (!char.IsAsciiLetter(c0) || !char.IsAsciiLetter(c1))
        {
            return null;
        }

        return new string(
            new[] { char.ToUpperInvariant(c0), char.ToUpperInvariant(c1) });
    }

    public bool Equals(DirectCountryCode? other) =>
        other is not null &&
        string.Equals(Code, other.Code, StringComparison.Ordinal);

    public override bool Equals(object? obj) =>
        Equals(obj as DirectCountryCode);

    public override int GetHashCode() =>
        Code.GetHashCode(StringComparison.Ordinal);

    public override string ToString() => Code;

    public static bool operator ==(
        DirectCountryCode? left,
        DirectCountryCode? right) =>
        Equals(left, right);

    public static bool operator !=(
        DirectCountryCode? left,
        DirectCountryCode? right) =>
        !Equals(left, right);

    // Centralized immutable ISO 3166-1 alpha-2 catalog. This is the single
    // authority for "is this a real country code?"; production code must never
    // re-implement this check. Intentionally not limited to IR/IQ/RO.
    private static readonly HashSet<string> KnownCodes = new(
        StringComparer.Ordinal)
    {
        "AD", "AE", "AF", "AG", "AI", "AL", "AM", "AO", "AQ", "AR",
        "AS", "AT", "AU", "AW", "AX", "AZ", "BA", "BB", "BD", "BE",
        "BF", "BG", "BH", "BI", "BJ", "BL", "BM", "BN", "BO", "BQ",
        "BR", "BS", "BT", "BV", "BW", "BY", "BZ", "CA", "CC", "CD",
        "CF", "CG", "CH", "CI", "CK", "CL", "CM", "CN", "CO", "CR",
        "CU", "CV", "CW", "CX", "CY", "CZ", "DE", "DJ", "DK", "DM",
        "DO", "DZ", "EC", "EE", "EG", "EH", "ER", "ES", "ET", "FI",
        "FJ", "FK", "FM", "FO", "FR", "GA", "GB", "GD", "GE", "GF",
        "GG", "GH", "GI", "GL", "GM", "GN", "GP", "GQ", "GR", "GS",
        "GT", "GU", "GW", "GY", "HK", "HM", "HN", "HR", "HT", "HU",
        "ID", "IE", "IL", "IM", "IN", "IO", "IQ", "IR", "IS", "IT",
        "JE", "JM", "JO", "JP", "KE", "KG", "KH", "KI", "KM", "KN",
        "KP", "KR", "KW", "KY", "KZ", "LA", "LB", "LC", "LI", "LK",
        "LR", "LS", "LT", "LU", "LV", "LY", "MA", "MC", "MD", "ME",
        "MF", "MG", "MH", "MK", "ML", "MM", "MN", "MO", "MP", "MQ",
        "MR", "MS", "MT", "MU", "MV", "MW", "MX", "MY", "MZ", "NA",
        "NC", "NE", "NF", "NG", "NI", "NL", "NO", "NP", "NR", "NU",
        "NZ", "OM", "PA", "PE", "PF", "PG", "PH", "PK", "PL", "PM",
        "PN", "PR", "PS", "PT", "PW", "PY", "QA", "RE", "RO", "RS",
        "RU", "RW", "SA", "SB", "SC", "SD", "SE", "SG", "SH", "SI",
        "SJ", "SK", "SL", "SM", "SN", "SO", "SR", "SS", "ST", "SV",
        "SX", "SY", "SZ", "TC", "TD", "TF", "TG", "TH", "TJ", "TK",
        "TL", "TM", "TN", "TO", "TR", "TT", "TV", "TW", "TZ", "UA",
        "UG", "UM", "US", "UY", "UZ", "VA", "VC", "VE", "VG", "VI",
        "VN", "VU", "WF", "WS", "YE", "YT", "ZA", "ZM", "ZW"
    };
}
