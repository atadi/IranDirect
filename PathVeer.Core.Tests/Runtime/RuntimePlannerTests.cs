using PathVeer.Core.Configuration;
using PathVeer.Core.Runtime;

namespace PathVeer.Core.Tests.Runtime;

public sealed class RuntimePlannerTests
{
    private readonly RuntimePlanner _planner =
        new(new DesiredConfigurationValidator());

    [Fact]
    public void Plan_WhenEnabledAndHealthy_ProducesDesiredRoutes()
    {
        DesiredRuntime result =
            _planner.Plan(
                ConfigurationDefaults.Create() with
                {
                    Enabled = true
                },
                CreateHealthyObservedRuntime());

        Assert.True(result.Enabled);
        Assert.True(result.CanReconcile);
        Assert.Empty(result.Blockers);

        DesiredEndpointRoute endpoint =
            Assert.Single(result.EndpointRoutes);

        Assert.Equal(
            "5.160.74.148/32",
            endpoint.DestinationPrefix);
        Assert.Equal(1, endpoint.Metric);

        DesiredPrefixRoute prefix =
            Assert.Single(result.PrefixRoutes);

        Assert.Equal(
            "203.0.113.0/24",
            prefix.DestinationPrefix);
        Assert.Equal(5, prefix.Metric);
    }

    [Fact]
    public void Plan_WhenDisabled_KeepsEndpointProtectionButNoPrefixRoutes()
    {
        DesiredRuntime result =
            _planner.Plan(
                ConfigurationDefaults.Create() with
                {
                    Enabled = false
                },
                CreateHealthyObservedRuntime());

        Assert.False(result.Enabled);
        Assert.True(result.CanReconcile);
        Assert.Single(result.EndpointRoutes);
        Assert.Empty(result.PrefixRoutes);
    }

    [Fact]
    public void Plan_WhenEnabledAndProfileMissing_BlocksPrefixRoutes()
    {
        ObservedRuntime observed =
            CreateHealthyObservedRuntime() with
            {
                VpnProfileExists = false,
                VpnProfileValid = false,
                VpnEndpoints = []
            };

        DesiredRuntime result =
            _planner.Plan(
                ConfigurationDefaults.Create() with
                {
                    Enabled = true
                },
                observed);

        Assert.False(result.CanReconcile);
        Assert.Empty(result.EndpointRoutes);
        Assert.Empty(result.PrefixRoutes);
        Assert.Contains(
            result.Blockers,
            blocker =>
                blocker.Code ==
                RuntimeBlockerCode.VpnProfileUnavailable);
    }

    [Fact]
    public void Plan_WhenEnabledAndGatewayMissing_BlocksAllRoutes()
    {
        ObservedRuntime observed =
            CreateHealthyObservedRuntime() with
            {
                DirectGateway = null
            };

        DesiredRuntime result =
            _planner.Plan(
                ConfigurationDefaults.Create() with
                {
                    Enabled = true
                },
                observed);

        Assert.False(result.CanReconcile);
        Assert.Empty(result.EndpointRoutes);
        Assert.Empty(result.PrefixRoutes);
        Assert.Contains(
            result.Blockers,
            blocker =>
                blocker.Code ==
                RuntimeBlockerCode.DirectGatewayUnavailable);
    }

    [Fact]
    public void Plan_WhenEnabledAndPrefixesMissing_BlocksPrefixRoutes()
    {
        ObservedRuntime observed =
            CreateHealthyObservedRuntime() with
            {
                Prefixes = []
            };

        DesiredRuntime result =
            _planner.Plan(
                ConfigurationDefaults.Create() with
                {
                    Enabled = true
                },
                observed);

        Assert.False(result.CanReconcile);
        Assert.Single(result.EndpointRoutes);
        Assert.Empty(result.PrefixRoutes);
        Assert.Contains(
            result.Blockers,
            blocker =>
                blocker.Code ==
                RuntimeBlockerCode.PrefixesUnavailable);
    }

    [Fact]
    public void Plan_DeduplicatesEquivalentDesiredRoutes()
    {
        ObservedRuntime observed =
            CreateHealthyObservedRuntime() with
            {
                VpnEndpoints =
                [
                    new ObservedVpnEndpoint
                    {
                        Host = "vpn-a.example",
                        Address = "5.160.74.148",
                        Port = 1409,
                        Protocol = "tcp"
                    },
                    new ObservedVpnEndpoint
                    {
                        Host = "vpn-b.example",
                        Address = "5.160.74.148",
                        Port = 1409,
                        Protocol = "tcp"
                    }
                ],
                Prefixes =
                [
                    "203.0.113.0/24",
                    "203.0.113.0/24"
                ]
            };

        DesiredRuntime result =
            _planner.Plan(
                ConfigurationDefaults.Create() with
                {
                    Enabled = true
                },
                observed);

        Assert.Single(result.EndpointRoutes);
        Assert.Single(result.PrefixRoutes);
    }

    private static ObservedRuntime CreateHealthyObservedRuntime()
    {
        return new ObservedRuntime
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
            ObservedAt = DateTimeOffset.UtcNow
        };
    }
}