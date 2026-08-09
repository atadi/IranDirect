using System.Text.Json;
using PathVeer.Core.Installation;
using PathVeer.Core.Vpn;

namespace PathVeer.Core.Tests.Installation;

/// <summary>
/// Phase 36.7 — persisted schema and product-identity compatibility.
///
/// These tests exist because two of the remaining "IranDirect" strings are NOT
/// branding defects: one is a persisted ownership flag whose name is load
/// bearing, and one is the legacy state root. Renaming either would break real
/// installations, so the compatibility is asserted rather than left to comment.
/// </summary>
public sealed class PersistedSchemaCompatibilityTests
{
    /// <summary>
    /// The exact serializer configuration the endpoint inventory is written
    /// with (see <c>JsonStore&lt;T&gt;</c>): no naming policy, so JSON member
    /// names equal C# property names.
    /// </summary>
    private static readonly JsonSerializerOptions StoreOptions =
        new() { WriteIndented = true };

    [Fact]
    public void OwnershipFlagSerializesAsAddedByIranDirect()
    {
        VpnEndpointInventoryItem item = CreateItem(addedByPathVeer: true);

        string json = JsonSerializer.Serialize(item, StoreOptions);

        // The persisted contract. Changing this string orphans managed routes
        // on every machine that upgraded from IranDirect.
        Assert.Contains("\"AddedByIranDirect\"", json);
    }

    [Fact]
    public void LegacyStateFileRoundTripsOwnershipAsTrue()
    {
        // A record shaped exactly like a real pre-rebrand
        // endpoint-inventory.json entry.
        const string legacyJson = """
        {
          "Host": "vpn.example.com",
          "Address": "203.0.113.10",
          "Port": 1194,
          "Protocol": "udp",
          "DestinationPrefix": "203.0.113.10/32",
          "Gateway": "192.168.1.1",
          "InterfaceIndex": 12,
          "Metric": 1,
          "AddedByIranDirect": true,
          "IsCurrent": true,
          "ProtectedAt": "2025-01-01T00:00:00+00:00",
          "LastSeenAt": "2025-01-01T00:00:00+00:00"
        }
        """;

        VpnEndpointInventoryItem? item =
            JsonSerializer.Deserialize<VpnEndpointInventoryItem>(
                legacyJson,
                StoreOptions);

        Assert.NotNull(item);

        // If this ever regresses to false, PathVeer stops recognising routes it
        // owns and silently leaves them applied.
        Assert.True(
            item!.AddedByIranDirect,
            "route ownership was lost when reading a legacy inventory entry");
    }

    [Fact]
    public void OwnershipSurvivesASaveLoadCycle()
    {
        VpnEndpointInventoryItem original = CreateItem(addedByPathVeer: true);

        string json = JsonSerializer.Serialize(original, StoreOptions);

        VpnEndpointInventoryItem? restored =
            JsonSerializer.Deserialize<VpnEndpointInventoryItem>(
                json,
                StoreOptions);

        Assert.True(restored!.AddedByIranDirect);
    }

    [Fact]
    public void UnownedEndpointStaysUnowned()
    {
        VpnEndpointInventoryItem item = CreateItem(addedByPathVeer: false);

        string json = JsonSerializer.Serialize(item, StoreOptions);

        VpnEndpointInventoryItem? restored =
            JsonSerializer.Deserialize<VpnEndpointInventoryItem>(
                json,
                StoreOptions);

        Assert.False(restored!.AddedByIranDirect);
    }

    // ---------------- product identity ----------------

    [Fact]
    public void UserAgentIsPathVeerBranded()
    {
        Assert.StartsWith("PathVeer/", ProductIdentity.UserAgent);

        Assert.DoesNotContain(
            "IranDirect",
            ProductIdentity.UserAgent,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UserAgentCarriesARealVersionNotAHardcodedOnePointZero()
    {
        // The legacy string was literally "IranDirect/1.0" regardless of build.
        string version = ProductIdentity.Version;

        Assert.False(string.IsNullOrWhiteSpace(version));
        Assert.Matches(@"^\d+\.\d+", version);
    }

    [Fact]
    public void UserAgentDoesNotLeakBuildMetadata()
    {
        // A '+' suffix would expose the source revision to remote prefix
        // sources; it is stripped deliberately.
        Assert.DoesNotContain('+', ProductIdentity.UserAgent);
    }

    private static VpnEndpointInventoryItem CreateItem(bool addedByPathVeer) =>
        new()
        {
            Host = "vpn.example.com",
            Address = "203.0.113.10",
            Port = 1194,
            Protocol = "udp",
            DestinationPrefix = "203.0.113.10/32",
            Gateway = "192.168.1.1",
            InterfaceIndex = 12,
            AddedByIranDirect = addedByPathVeer,
            ProtectedAt = DateTimeOffset.UnixEpoch,
            LastSeenAt = DateTimeOffset.UnixEpoch
        };
}
