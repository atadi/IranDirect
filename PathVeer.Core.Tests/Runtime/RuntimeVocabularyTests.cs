using PathVeer.Core.Runtime;

namespace PathVeer.Core.Tests.Runtime;

public sealed class RuntimeVocabularyTests
{
    [Fact]
    public void DesiredRuntime_WithNoBlockers_CanReconcile()
    {
        DesiredRuntime runtime = new()
        {
            Enabled = true
        };

        Assert.True(runtime.CanReconcile);
    }

    [Fact]
    public void DesiredRuntime_WithBlocker_CannotReconcile()
    {
        DesiredRuntime runtime = new()
        {
            Enabled = true,
            Blockers =
            [
                new RuntimeBlocker
                {
                    Code =
                        RuntimeBlockerCode.VpnProfileUnavailable,
                    Message = "VPN profile is unavailable."
                }
            ]
        };

        Assert.False(runtime.CanReconcile);
    }

    [Fact]
    public void DesiredRoutes_UseStableRouteIdentity()
    {
        DesiredEndpointRoute endpoint = new()
        {
            Host = "vpn.example",
            Address = "5.160.74.148",
            Port = 1409,
            Protocol = "tcp",
            DestinationPrefix = "5.160.74.148/32",
            Gateway = "192.168.100.1",
            InterfaceIndex = 30
        };

        DesiredPrefixRoute prefix = new()
        {
            DestinationPrefix = "203.0.113.0/24",
            Gateway = "192.168.100.1",
            InterfaceIndex = 30
        };

        Assert.Equal(
            "5.160.74.148/32|192.168.100.1|30",
            endpoint.Identity);

        Assert.Equal(
            "203.0.113.0/24|192.168.100.1|30",
            prefix.Identity);
    }

    [Fact]
    public void ObservedRuntime_IsAReadOnlySnapshot()
    {
        DateTimeOffset observedAt =
            DateTimeOffset.UtcNow;

        ObservedRuntime runtime = new()
        {
            VpnProfileExists = true,
            VpnProfileValid = true,
            DirectGateway =
                new ObservedDirectGateway
                {
                    Address = "192.168.100.1",
                    InterfaceIndex = 30,
                    InterfaceName = "Ethernet",
                    InterfaceMetric = 10
                },
            VpnEndpoints =
            [
                new ObservedVpnEndpoint
                {
                    Host = "5.160.74.148",
                    Address = "5.160.74.148",
                    Port = 1409,
                    Protocol = "tcp"
                }
            ],
            Prefixes =
            [
                "203.0.113.0/24"
            ],
            ObservedAt = observedAt
        };

        Assert.Equal(observedAt, runtime.ObservedAt);
        Assert.Single(runtime.VpnEndpoints);
        Assert.Single(runtime.Prefixes);
        Assert.NotNull(runtime.DirectGateway);
    }
}