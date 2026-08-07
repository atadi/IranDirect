using System.Reflection;
using IranDirect.Core.Configuration;
using IranDirect.Core.Networking;
using IranDirect.Core.Prefixes;
using IranDirect.Core.Routing;
using IranDirect.Core.State;
using IranDirect.Core.Vpn;

namespace IranDirect.Core.Diagnostics;

public sealed class IranDirectDiagnosticsService
{
    private readonly string _profilePath;
    private readonly CountryPrefixStore _prefixStore;
    private readonly Func<DirectCountryCode> _countryResolver;
    private readonly StateRepository _stateRepository;
    private readonly RouteInventoryStore _routeInventoryStore;
    private readonly VpnEndpointInventoryStore
        _endpointInventoryStore;
    private readonly GatewayDetector _gatewayDetector;
    private readonly OpenVpnEndpointProvider
        _vpnEndpointProvider;
    private readonly VpnEndpointRouteManager
        _endpointRouteManager;

    public IranDirectDiagnosticsService(
        string profilePath,
        CountryPrefixStore prefixStore,
        Func<DirectCountryCode> countryResolver,
        StateRepository stateRepository,
        RouteInventoryStore routeInventoryStore,
        VpnEndpointInventoryStore endpointInventoryStore,
        GatewayDetector gatewayDetector,
        OpenVpnEndpointProvider vpnEndpointProvider,
        VpnEndpointRouteManager endpointRouteManager)
    {
        _profilePath = profilePath;
        _prefixStore = prefixStore;
        _countryResolver = countryResolver;
        _stateRepository = stateRepository;
        _routeInventoryStore = routeInventoryStore;
        _endpointInventoryStore = endpointInventoryStore;
        _gatewayDetector = gatewayDetector;
        _vpnEndpointProvider = vpnEndpointProvider;
        _endpointRouteManager = endpointRouteManager;
    }

    public async Task<IranDirectDiagnostics> RunAsync(
        CancellationToken cancellationToken = default)
    {
        List<DiagnosticCheck> checks = [];

        await CheckStateAsync(checks, cancellationToken);
        await CheckPrefixesAsync(checks, cancellationToken);
        CheckProfileExists(checks);
        await CheckProfileAndResolutionAsync(
            checks,
            cancellationToken);
        CheckGateway(checks);
        await CheckRouteInventoryAsync(
            checks,
            cancellationToken);
        await CheckEndpointInventoryAsync(
            checks,
            cancellationToken);

        DiagnosticSeverity overall =
            checks.Any(item =>
                item.Severity == DiagnosticSeverity.Fail)
                ? DiagnosticSeverity.Fail
                : checks.Any(item =>
                    item.Severity ==
                    DiagnosticSeverity.Warning)
                    ? DiagnosticSeverity.Warning
                    : DiagnosticSeverity.Pass;

        string version =
            Assembly.GetExecutingAssembly()
                .GetName()
                .Version?
                .ToString()
            ?? "Unknown";

        return new IranDirectDiagnostics
        {
            GeneratedAt = DateTimeOffset.UtcNow,
            Version = version,
            OverallSeverity = overall,
            Checks = checks
        };
    }

    private async Task CheckStateAsync(
        ICollection<DiagnosticCheck> checks,
        CancellationToken cancellationToken)
    {
        try
        {
            IranDirectState state =
                await _stateRepository.LoadAsync(
                    cancellationToken);

            checks.Add(Pass(
                "State",
                $"Readable. Enabled: {state.Enabled}."));
        }
        catch (Exception exception)
        {
            checks.Add(Fail(
                "State",
                $"Could not read state.json: " +
                $"{exception.Message}"));
        }
    }

    private async Task CheckPrefixesAsync(
        ICollection<DiagnosticCheck> checks,
        CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<string> prefixes =
                await _prefixStore.LoadPrefixesAsync(
                    _countryResolver(),
                    cancellationToken);

            checks.Add(
                prefixes.Count > 0
                    ? Pass(
                        "Prefixes",
                        $"{prefixes.Count} prefixes loaded.")
                    : Warning(
                        "Prefixes",
                        "No IPv4 prefixes are available."));
        }
        catch (Exception exception)
        {
            checks.Add(Fail(
                "Prefixes",
                $"Could not read the prefix file: " +
                $"{exception.Message}"));
        }
    }

    private void CheckProfileExists(
        ICollection<DiagnosticCheck> checks)
    {
        checks.Add(
            File.Exists(_profilePath)
                ? Pass(
                    "VPN profile",
                    _profilePath)
                : Fail(
                    "VPN profile",
                    $"Profile not found: {_profilePath}"));
    }

    private async Task CheckProfileAndResolutionAsync(
        ICollection<DiagnosticCheck> checks,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(_profilePath))
        {
            return;
        }

        try
        {
            IReadOnlyList<ResolvedVpnEndpoint> endpoints =
                await _vpnEndpointProvider.GetEndpointsAsync(
                    cancellationToken);

            string text = string.Join(
                ", ",
                endpoints.Select(endpoint =>
                    $"{endpoint.Address}:" +
                    $"{endpoint.Port}/" +
                    $"{endpoint.Protocol}"));

            checks.Add(Pass(
                "VPN endpoint resolution",
                text));
        }
        catch (Exception exception)
        {
            checks.Add(Fail(
                "VPN endpoint resolution",
                exception.Message));
        }
    }

    private void CheckGateway(
        ICollection<DiagnosticCheck> checks)
    {
        try
        {
            var gateway = _gatewayDetector.Detect();

            checks.Add(Pass(
                "Direct gateway",
                $"{gateway.Address} via " +
                $"{gateway.InterfaceName} " +
                $"({gateway.InterfaceIndex})."));
        }
        catch (Exception exception)
        {
            checks.Add(Fail(
                "Direct gateway",
                exception.Message));
        }
    }

    private async Task CheckRouteInventoryAsync(
        ICollection<DiagnosticCheck> checks,
        CancellationToken cancellationToken)
    {
        try
        {
            RouteInventory inventory =
                await _routeInventoryStore.LoadAsync(
                    cancellationToken);

            checks.Add(Pass(
                "Route inventory",
                $"{inventory.Routes.Count} owned route(s)."));
        }
        catch (Exception exception)
        {
            checks.Add(Fail(
                "Route inventory",
                $"Could not read route-inventory.json: " +
                $"{exception.Message}"));
        }
    }

    private async Task CheckEndpointInventoryAsync(
        ICollection<DiagnosticCheck> checks,
        CancellationToken cancellationToken)
    {
        try
        {
            VpnEndpointInventory inventory =
                await _endpointInventoryStore.LoadAsync(
                    cancellationToken);

            VpnEndpointProtectionHealth health =
                await _endpointRouteManager.GetHealthAsync(
                    inventory.Endpoints,
                    cancellationToken);

            if (health.CurrentEndpointCount == 0)
            {
                checks.Add(Warning(
                    "Endpoint protection",
                    "No current endpoint inventory exists."));
                return;
            }

            checks.Add(
                health.IsProtected
                    ? Pass(
                        "Endpoint protection",
                        $"{health.ProtectedEndpointCount}/" +
                        $"{health.CurrentEndpointCount} " +
                        "current endpoint(s) protected.")
                    : Fail(
                        "Endpoint protection",
                        $"{health.ProtectedEndpointCount}/" +
                        $"{health.CurrentEndpointCount} " +
                        "current endpoint(s) protected."));
        }
        catch (Exception exception)
        {
            checks.Add(Fail(
                "Endpoint inventory",
                $"Could not validate endpoint protection: " +
                $"{exception.Message}"));
        }
    }

    private static DiagnosticCheck Pass(
        string name,
        string message) =>
        new()
        {
            Name = name,
            Severity = DiagnosticSeverity.Pass,
            Message = message
        };

    private static DiagnosticCheck Warning(
        string name,
        string message) =>
        new()
        {
            Name = name,
            Severity = DiagnosticSeverity.Warning,
            Message = message
        };

    private static DiagnosticCheck Fail(
        string name,
        string message) =>
        new()
        {
            Name = name,
            Severity = DiagnosticSeverity.Fail,
            Message = message
        };
}