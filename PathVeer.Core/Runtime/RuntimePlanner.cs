using PathVeer.Core.Configuration;

namespace PathVeer.Core.Runtime;

public sealed class RuntimePlanner
{
    private const int EndpointRouteMetric = 1;
    private const int PrefixRouteMetric = 5;

    private readonly DesiredConfigurationValidator _validator;

    public RuntimePlanner(
        DesiredConfigurationValidator validator)
    {
        _validator = validator;
    }

    public DesiredRuntime Plan(
        DesiredConfiguration configuration,
        ObservedRuntime observed)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(observed);

        List<RuntimeBlocker> blockers = [];

        ConfigurationValidationResult validation =
            _validator.Validate(configuration);

        foreach (string error in validation.Errors)
        {
            blockers.Add(
                new RuntimeBlocker
                {
                    Code =
                        RuntimeBlockerCode.InvalidConfiguration,
                    Message = error
                });
        }

        bool profileUsable =
            observed.VpnProfileExists &&
            observed.VpnProfileValid;

        if (configuration.Enabled)
        {
            if (!observed.VpnProfileExists)
            {
                blockers.Add(
                    new RuntimeBlocker
                    {
                        Code =
                            RuntimeBlockerCode.VpnProfileUnavailable,
                        Message =
                            "The configured VPN profile does not exist."
                    });
            }
            else if (!observed.VpnProfileValid)
            {
                blockers.Add(
                    new RuntimeBlocker
                    {
                        Code =
                            RuntimeBlockerCode.VpnProfileUnavailable,
                        Message =
                            "The configured VPN profile is invalid."
                    });
            }

            if (observed.VpnEndpoints.Count == 0)
            {
                blockers.Add(
                    new RuntimeBlocker
                    {
                        Code =
                            RuntimeBlockerCode.VpnEndpointUnavailable,
                        Message =
                            "No IPv4 VPN endpoints are available."
                    });
            }

            if (observed.DirectGateway is null)
            {
                blockers.Add(
                    new RuntimeBlocker
                    {
                        Code =
                            RuntimeBlockerCode.DirectGatewayUnavailable,
                        Message =
                            "No direct ISP gateway is available."
                    });
            }

            if (observed.Prefixes.Count == 0)
            {
                blockers.Add(
                    new RuntimeBlocker
                    {
                        Code =
                            RuntimeBlockerCode.PrefixesUnavailable,
                        Message =
                            "No direct-routing prefixes are available."
                    });
            }
        }

        IReadOnlyList<DesiredEndpointRoute> endpointRoutes =
            BuildEndpointRoutes(
                observed,
                profileUsable);

        IReadOnlyList<DesiredPrefixRoute> prefixRoutes =
            configuration.Enabled &&
            blockers.Count == 0
                ? BuildPrefixRoutes(observed)
                : [];

        return new DesiredRuntime
        {
            Enabled = configuration.Enabled,
            EndpointRoutes = endpointRoutes,
            PrefixRoutes = prefixRoutes,
            Blockers = blockers
        };
    }

    private static IReadOnlyList<DesiredEndpointRoute>
        BuildEndpointRoutes(
            ObservedRuntime observed,
            bool profileUsable)
    {
        if (!profileUsable ||
            observed.DirectGateway is null)
        {
            return [];
        }

        ObservedDirectGateway gateway =
            observed.DirectGateway;

        return observed.VpnEndpoints
            .Select(endpoint =>
                new DesiredEndpointRoute
                {
                    Host = endpoint.Host,
                    Address = endpoint.Address,
                    Port = endpoint.Port,
                    Protocol = endpoint.Protocol,
                    DestinationPrefix =
                        $"{endpoint.Address}/32",
                    Gateway = gateway.Address,
                    InterfaceIndex =
                        gateway.InterfaceIndex,
                    Metric = EndpointRouteMetric
                })
            .GroupBy(
                route => route.Identity,
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
    }

    private static IReadOnlyList<DesiredPrefixRoute>
        BuildPrefixRoutes(
            ObservedRuntime observed)
    {
        ObservedDirectGateway gateway =
            observed.DirectGateway
            ?? throw new InvalidOperationException(
                "A direct gateway is required.");

        return observed.Prefixes
            .Select(prefix =>
                new DesiredPrefixRoute
                {
                    DestinationPrefix = prefix,
                    Gateway = gateway.Address,
                    InterfaceIndex =
                        gateway.InterfaceIndex,
                    Metric = PrefixRouteMetric
                })
            .GroupBy(
                route => route.Identity,
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
    }
}