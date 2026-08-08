using System.Text.Json.Serialization;

namespace PathVeer.Core.Configuration;

public sealed record DesiredConfiguration
{
    public int SchemaVersion { get; init; } = 1;

    public bool Enabled { get; init; }

    public VpnProviderType VpnProvider { get; init; } =
        VpnProviderType.OpenVpn;

    public string VpnProfilePath { get; init; } =
        "vpn-profile.ovpn";

    public bool AutoRepair { get; init; } = true;

    public TimeSpan RepairInterval { get; init; } =
        TimeSpan.FromSeconds(30);

    public bool AutoUpdatePrefixes { get; init; } = true;

    public TimeSpan PrefixUpdateInterval { get; init; } =
        TimeSpan.FromDays(1);

    /// <summary>
    /// The destination country whose IP prefixes should bypass the VPN and use
    /// the direct/local route. This is routing policy, not the client's
    /// physical location, VPN exit, nationality, locale, language, or timezone.
    ///
    /// Persisted as a plain ISO 3166-1 alpha-2 string. A legacy configuration
    /// without this field (and <c>null</c> in memory) is interpreted as
    /// <c>IR</c> for backward compatibility, so existing installations continue
    /// to behave exactly as before.
    /// </summary>
    [JsonConverter(typeof(DirectCountryCodeJsonConverter))]
    public DirectCountryCode? DirectCountryCode { get; init; } =
        DirectCountryCode.IR;
}
