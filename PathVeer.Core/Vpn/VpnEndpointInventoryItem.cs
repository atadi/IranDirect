using System.Text.Json.Serialization;

namespace PathVeer.Core.Vpn;

public sealed record VpnEndpointInventoryItem
{
    public required string Host { get; init; }

    public required string Address { get; init; }

    public required int Port { get; init; }

    public required string Protocol { get; init; }

    public required string DestinationPrefix { get; init; }

    public required string Gateway { get; init; }

    public required uint InterfaceIndex { get; init; }

    public int Metric { get; init; } = 1;

    /// <summary>
    /// True when PathVeer (or its predecessor, IranDirect) created this
    /// endpoint-protection route, which makes it safe to remove.
    ///
    /// PHASE 36.7 SCHEMA DECISION — the serialized name stays
    /// <c>AddedByIranDirect</c> forever.
    ///
    /// This is a PERSISTED ownership flag in
    /// <c>%ProgramData%\...\endpoint-inventory.json</c>. It is written with the
    /// default <see cref="System.Text.Json"/> contract (no naming policy), so
    /// the JSON member name is literally the C# property name, and existing
    /// state files on real machines contain <c>"AddedByIranDirect"</c>.
    ///
    /// Renaming the property would make deserialization silently fall back to
    /// <c>false</c> for every previously protected endpoint. PathVeer would
    /// then conclude it does not own those routes and would stop reconciling
    /// them, orphaning live routes on the machine. That is a correctness and
    /// safety failure, not a cosmetic one.
    ///
    /// The <see cref="JsonPropertyNameAttribute"/> below pins the wire name
    /// explicitly so that any future rename of the C# property cannot change
    /// the persisted contract by accident.
    /// </summary>
    [JsonPropertyName("AddedByIranDirect")]
    public bool AddedByIranDirect { get; init; }

    public bool IsCurrent { get; init; } = true;

    public DateTimeOffset ProtectedAt { get; init; }

    public DateTimeOffset LastSeenAt { get; init; }

    [JsonIgnore]
    public string Identity =>
        $"{DestinationPrefix}|{Gateway}|{InterfaceIndex}";
}