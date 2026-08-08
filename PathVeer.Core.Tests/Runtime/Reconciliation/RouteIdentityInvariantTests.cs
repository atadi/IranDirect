using System.Net;
using System.Text.Json;
using PathVeer.Core.Ipc;
using PathVeer.Core.Routing;
using PathVeer.Core.Runtime;
using PathVeer.Core.Vpn;

namespace PathVeer.Core.Tests.Runtime.Reconciliation;

/// <summary>
/// Pins the invariant that every computed route <c>Identity</c> always
/// reflects the record's CURRENT identity-bearing properties, including
/// after a <c>with</c> expression.
///
/// Phase 31.1 proved that a lazily cached identity backing field returns a
/// STALE value after <c>with</c>, because the compiler-generated record copy
/// constructor copies private fields before <c>init</c> assignment runs.
/// These tests exist so that defect cannot be introduced silently, and are
/// meaningful independently of any optimization: they document the
/// established identity contract for the route models.
///
/// Identity formula for every type covered here:
///   $"{DestinationPrefix}|{Gateway-or-NextHop}|{InterfaceIndex}"
/// Metric is deliberately NOT part of the formula.
/// </summary>
public sealed class RouteIdentityInvariantTests
{
    private const string Prefix = "203.0.113.0/24";
    private const string OtherPrefix = "198.51.100.0/24";
    private const string Gateway = "192.168.100.1";
    private const string OtherGateway = "10.10.10.254";

    private static ObservedRoute Observed() => new()
    {
        DestinationPrefix = Prefix,
        NextHop = Gateway,
        InterfaceIndex = 30,
        Metric = 5
    };

    private static DesiredPrefixRoute DesiredPrefix() => new()
    {
        DestinationPrefix = Prefix,
        Gateway = Gateway,
        InterfaceIndex = 30,
        Metric = 5
    };

    private static DesiredEndpointRoute DesiredEndpoint() => new()
    {
        Host = "vpn.example",
        Address = "5.160.74.148",
        Port = 1409,
        Protocol = "tcp",
        DestinationPrefix = Prefix,
        Gateway = Gateway,
        InterfaceIndex = 30,
        Metric = 1
    };

    private static RouteInventoryItem InventoryItem() => new()
    {
        DestinationPrefix = Prefix,
        Gateway = Gateway,
        InterfaceIndex = 30,
        Metric = 5
    };

    private static VpnEndpointInventoryItem VpnItem() => new()
    {
        Host = "vpn.example",
        Address = "5.160.74.148",
        Port = 1409,
        Protocol = "tcp",
        DestinationPrefix = Prefix,
        Gateway = Gateway,
        InterfaceIndex = 30,
        Metric = 1
    };

    private static ManagedRoute Managed() => new()
    {
        DestinationPrefix = Prefix,
        Gateway = IPAddress.Parse(Gateway),
        InterfaceIndex = 30,
        Metric = 5
    };

    /// <summary>
    /// Reads <c>Identity</c> and returns the record unchanged, so that any
    /// lazily cached identity is populated before a <c>with</c> copy.
    /// </summary>
    private static T Primed<T>(T route, Func<T, string> identity)
    {
        _ = identity(route);
        return route;
    }

    // ---- Baseline: exact expected identity strings ----

    [Fact]
    public void Identity_MatchesExactExpectedString()
    {
        const string expected = "203.0.113.0/24|192.168.100.1|30";

        Assert.Equal(expected, Observed().Identity);
        Assert.Equal(expected, DesiredPrefix().Identity);
        Assert.Equal(expected, DesiredEndpoint().Identity);
        Assert.Equal(expected, InventoryItem().Identity);
        Assert.Equal(expected, VpnItem().Identity);
        Assert.Equal(expected, Managed().Identity);
    }

    [Fact]
    public void Identity_SameFieldsProduceEqualIdentity_AcrossInstances()
    {
        Assert.Equal(Observed().Identity, Observed().Identity);
        Assert.Equal(DesiredPrefix().Identity, DesiredPrefix().Identity);
        Assert.Equal(DesiredEndpoint().Identity, DesiredEndpoint().Identity);
    }

    [Fact]
    public void Identity_RepeatedReadsReturnEqualValues()
    {
        ObservedRoute observed = Observed();
        DesiredPrefixRoute prefix = DesiredPrefix();
        DesiredEndpointRoute endpoint = DesiredEndpoint();

        for (int i = 0; i < 10; i++)
        {
            Assert.Equal("203.0.113.0/24|192.168.100.1|30", observed.Identity);
            Assert.Equal("203.0.113.0/24|192.168.100.1|30", prefix.Identity);
            Assert.Equal("203.0.113.0/24|192.168.100.1|30", endpoint.Identity);
        }
    }

    /// <summary>
    /// Applies one per-type <c>with</c> mutation across every covered record
    /// and asserts the resulting identity. Each source record is primed by
    /// reading <c>Identity</c> first, so a lazily cached identity would be
    /// populated before the copy and would surface here as a stale value.
    /// </summary>
    private static void AssertIdentityAfterWith(
        string expected,
        Func<ObservedRoute, ObservedRoute> observed,
        Func<DesiredPrefixRoute, DesiredPrefixRoute> prefix,
        Func<DesiredEndpointRoute, DesiredEndpointRoute> endpoint,
        Func<RouteInventoryItem, RouteInventoryItem> inventory,
        Func<VpnEndpointInventoryItem, VpnEndpointInventoryItem> vpn,
        Func<ManagedRoute, ManagedRoute> managed)
    {
        Assert.Equal(
            expected,
            observed(Primed(Observed(), r => r.Identity)).Identity);
        Assert.Equal(
            expected,
            prefix(Primed(DesiredPrefix(), r => r.Identity)).Identity);
        Assert.Equal(
            expected,
            endpoint(Primed(DesiredEndpoint(), r => r.Identity)).Identity);
        Assert.Equal(
            expected,
            inventory(Primed(InventoryItem(), r => r.Identity)).Identity);
        Assert.Equal(
            expected,
            vpn(Primed(VpnItem(), r => r.Identity)).Identity);
        Assert.Equal(
            expected,
            managed(Primed(Managed(), r => r.Identity)).Identity);
    }

    // ---- with: identity-bearing fields participate ----

    [Fact]
    public void With_ChangedDestinationPrefix_ChangesIdentity() =>
        AssertIdentityAfterWith(
            "198.51.100.0/24|192.168.100.1|30",
            r => r with { DestinationPrefix = OtherPrefix },
            r => r with { DestinationPrefix = OtherPrefix },
            r => r with { DestinationPrefix = OtherPrefix },
            r => r with { DestinationPrefix = OtherPrefix },
            r => r with { DestinationPrefix = OtherPrefix },
            r => r with { DestinationPrefix = OtherPrefix });

    [Fact]
    public void With_ChangedGatewayOrNextHop_ChangesIdentity() =>
        AssertIdentityAfterWith(
            "203.0.113.0/24|10.10.10.254|30",
            r => r with { NextHop = OtherGateway },
            r => r with { Gateway = OtherGateway },
            r => r with { Gateway = OtherGateway },
            r => r with { Gateway = OtherGateway },
            r => r with { Gateway = OtherGateway },
            r => r with { Gateway = IPAddress.Parse(OtherGateway) });

    [Fact]
    public void With_ChangedInterfaceIndex_ChangesIdentity() =>
        AssertIdentityAfterWith(
            "203.0.113.0/24|192.168.100.1|77",
            r => r with { InterfaceIndex = 77u },
            r => r with { InterfaceIndex = 77u },
            r => r with { InterfaceIndex = 77u },
            r => r with { InterfaceIndex = 77u },
            r => r with { InterfaceIndex = 77u },
            r => r with { InterfaceIndex = 77u });

    // ---- Metric does NOT participate ----

    [Fact]
    public void With_ChangedMetric_DoesNotChangeIdentity()
    {
        AssertIdentityAfterWith(
            "203.0.113.0/24|192.168.100.1|30",
            r => r with { Metric = 999 },
            r => r with { Metric = 999 },
            r => r with { Metric = 999 },
            r => r with { Metric = 999 },
            r => r with { Metric = 999 },
            r => r with { Metric = 999 });

        // Identity-equal but record-unequal: the planner relies on this to
        // treat metric-only variants as duplicate identities.
        Assert.NotEqual(Observed(), Observed() with { Metric = 999 });
        Assert.NotEqual(DesiredPrefix(), DesiredPrefix() with { Metric = 999 });
        Assert.NotEqual(
            DesiredEndpoint(),
            DesiredEndpoint() with { Metric = 999 });
    }

    // ---- Non-identity field changes preserve Identity ----

    [Fact]
    public void With_NoIdentityFieldChange_PreservesIdentity()
    {
        const string expected = "203.0.113.0/24|192.168.100.1|30";

        DesiredEndpointRoute endpoint = DesiredEndpoint();
        _ = endpoint.Identity;

        Assert.Equal(
            expected,
            (endpoint with { Host = "other.example" }).Identity);
        Assert.Equal(
            expected,
            (endpoint with { Port = 8443 }).Identity);
        Assert.Equal(
            expected,
            (endpoint with { Protocol = "udp" }).Identity);

        VpnEndpointInventoryItem vpn = VpnItem();
        _ = vpn.Identity;

        Assert.Equal(
            expected,
            (vpn with { AddedByIranDirect = true }).Identity);
        Assert.Equal(
            expected,
            (vpn with { IsCurrent = false }).Identity);

        // An identical copy with no changes at all.
        Assert.Equal(expected, (endpoint with { }).Identity);
        Assert.Equal(expected, (vpn with { }).Identity);
    }

    // ---- Source object is unaffected by with ----

    [Fact]
    public void With_LeavesSourceRecordUnchanged()
    {
        const string expected = "203.0.113.0/24|192.168.100.1|30";

        ObservedRoute observed = Observed();
        DesiredPrefixRoute prefix = DesiredPrefix();
        DesiredEndpointRoute endpoint = DesiredEndpoint();

        _ = observed with { DestinationPrefix = OtherPrefix };
        _ = prefix with { Gateway = OtherGateway };
        _ = endpoint with { InterfaceIndex = 77u };

        Assert.Equal(Prefix, observed.DestinationPrefix);
        Assert.Equal(Gateway, observed.NextHop);
        Assert.Equal(30u, observed.InterfaceIndex);
        Assert.Equal(expected, observed.Identity);

        Assert.Equal(Gateway, prefix.Gateway);
        Assert.Equal(expected, prefix.Identity);

        Assert.Equal(30u, endpoint.InterfaceIndex);
        Assert.Equal(expected, endpoint.Identity);
    }

    // ---- Chained with expressions ----

    [Fact]
    public void With_ChainedChanges_ReflectFinalFieldValues()
    {
        ObservedRoute observed = Observed();
        _ = observed.Identity;

        ObservedRoute mutated = observed with
        {
            DestinationPrefix = OtherPrefix
        };
        _ = mutated.Identity;

        ObservedRoute twice = mutated with { NextHop = OtherGateway };
        _ = twice.Identity;

        ObservedRoute thrice = twice with { InterfaceIndex = 77u };

        Assert.Equal("198.51.100.0/24|10.10.10.254|77", thrice.Identity);
        Assert.Equal("203.0.113.0/24|192.168.100.1|30", observed.Identity);
    }

    // ---- Casing and whitespace are preserved verbatim ----

    [Fact]
    public void Identity_PreservesCasingAndWhitespaceVerbatim()
    {
        DesiredPrefixRoute upper = new()
        {
            DestinationPrefix = "2001:DB8::/32",
            Gateway = "FE80::1",
            InterfaceIndex = 30
        };
        Assert.Equal("2001:DB8::/32|FE80::1|30", upper.Identity);

        DesiredPrefixRoute lower = upper with
        {
            DestinationPrefix = "2001:db8::/32",
            Gateway = "fe80::1"
        };
        Assert.Equal("2001:db8::/32|fe80::1|30", lower.Identity);

        // Identity itself is case-SENSITIVE; case-insensitive matching is a
        // property of the consuming comparer, not of the formula.
        Assert.NotEqual(upper.Identity, lower.Identity);
        Assert.Equal(
            upper.Identity,
            lower.Identity,
            StringComparer.OrdinalIgnoreCase);

        DesiredPrefixRoute spaced = new()
        {
            DestinationPrefix = " 203.0.113.0/24 ",
            Gateway = " 192.168.100.1",
            InterfaceIndex = 30
        };
        Assert.Equal(" 203.0.113.0/24 | 192.168.100.1|30", spaced.Identity);
    }

    // ---- Boundary numeric formatting ----

    [Fact]
    public void Identity_FormatsInterfaceIndexBoundaries()
    {
        DesiredPrefixRoute zero = DesiredPrefix() with
        {
            InterfaceIndex = 0u
        };
        Assert.Equal("203.0.113.0/24|192.168.100.1|0", zero.Identity);

        DesiredPrefixRoute max = DesiredPrefix() with
        {
            InterfaceIndex = uint.MaxValue
        };
        Assert.Equal(
            "203.0.113.0/24|192.168.100.1|4294967295",
            max.Identity);
    }

    // ---- Identity is derived, never a separate source of truth ----

    [Fact]
    public void Identity_IsDerivedFromCurrentFields_NotStoredState()
    {
        DesiredPrefixRoute route = DesiredPrefix();

        // Build an equivalent record field-by-field and confirm the identity
        // depends only on the three identity-bearing fields.
        DesiredPrefixRoute rebuilt = new()
        {
            DestinationPrefix = route.DestinationPrefix,
            Gateway = route.Gateway,
            InterfaceIndex = route.InterfaceIndex,
            Metric = 12345
        };

        Assert.Equal(route.Identity, rebuilt.Identity);
        Assert.NotEqual(route, rebuilt);
    }

    // ---- Equality components are unchanged by identity being computed ----

    [Fact]
    public void Equality_RemainsBasedOnDeclaredComponents()
    {
        Assert.Equal(Observed(), Observed());
        Assert.Equal(Observed().GetHashCode(), Observed().GetHashCode());
        Assert.Equal(DesiredPrefix(), DesiredPrefix());
        Assert.Equal(DesiredEndpoint(), DesiredEndpoint());

        // Reading Identity must not alter equality or hash behavior.
        ObservedRoute read = Observed();
        ObservedRoute unread = Observed();
        _ = read.Identity;

        Assert.Equal(read, unread);
        Assert.Equal(read.GetHashCode(), unread.GetHashCode());

        // Identity-bearing field differences make records unequal.
        Assert.NotEqual(
            Observed(),
            Observed() with { DestinationPrefix = OtherPrefix });
        Assert.NotEqual(
            Observed(),
            Observed() with { NextHop = OtherGateway });
        Assert.NotEqual(
            Observed(),
            Observed() with { InterfaceIndex = 77u });
    }

    // ---- Serialization shape is unchanged ----

    [Fact]
    public void Serialization_IdentityIsExcludedFromPersistedInventoryItems()
    {
        string routeJson = JsonSerializer.Serialize(
            InventoryItem(),
            IranDirectJson.Options);
        string vpnJson = JsonSerializer.Serialize(
            VpnItem(),
            IranDirectJson.Options);

        // Both persisted item types carry [JsonIgnore] on Identity; the
        // persisted schema must not gain an Identity property.
        Assert.DoesNotContain("\"Identity\"", routeJson);
        Assert.DoesNotContain("\"Identity\"", vpnJson);

        // Identity-bearing fields themselves remain persisted.
        Assert.Contains("\"DestinationPrefix\"", routeJson);
        Assert.Contains("\"Gateway\"", routeJson);
        Assert.Contains("\"InterfaceIndex\"", routeJson);
    }

    [Fact]
    public void Serialization_InventoryRoundTripPreservesIdentity()
    {
        RouteInventoryItem original = InventoryItem();
        string json = JsonSerializer.Serialize(
            original,
            IranDirectJson.Options);

        RouteInventoryItem? restored =
            JsonSerializer.Deserialize<RouteInventoryItem>(
                json,
                IranDirectJson.Options);

        Assert.NotNull(restored);
        Assert.Equal(original.Identity, restored!.Identity);
        Assert.Equal(original, restored);

        // Identity remains correct after a with-expression on the restored
        // instance (a deserialized cache would otherwise start stale).
        Assert.Equal(
            "198.51.100.0/24|192.168.100.1|30",
            (restored with { DestinationPrefix = OtherPrefix }).Identity);
    }
}
