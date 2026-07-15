using IranDirect.Core.Models;
using IranDirect.Core.Networking;
using IranDirect.Core.Prefixes;
using IranDirect.Core.Routing;
using IranDirect.Core.State;
using System.Net;

namespace IranDirect.Core;

public sealed class IranDirectController
{
    private readonly IranPrefixProvider _prefixProvider;
    private readonly PrefixFileRepository _prefixRepository;
    private readonly GatewayDetector _gatewayDetector;
    private readonly RouteReconciler _routeReconciler;
    private readonly StateRepository _stateRepository;

    public IranDirectController(
        IranPrefixProvider prefixProvider,
        PrefixFileRepository prefixRepository,
        GatewayDetector gatewayDetector,
        RouteReconciler routeReconciler,
        StateRepository stateRepository)
    {
        _prefixProvider = prefixProvider;
        _prefixRepository = prefixRepository;
        _gatewayDetector = gatewayDetector;
        _routeReconciler = routeReconciler;
        _stateRepository = stateRepository;
    }

    public async Task<int> UpdatePrefixesAsync(
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<string> prefixes =
            await _prefixProvider.DownloadIpv4PrefixesAsync(
                cancellationToken);

        await _prefixRepository.SaveAsync(
            prefixes,
            cancellationToken);

        IranDirectState state =
            await _stateRepository.LoadAsync(
                cancellationToken);

        await _stateRepository.SaveAsync(
            state with
            {
                PrefixCount = prefixes.Count,
                PrefixesUpdatedAt =
                    DateTimeOffset.UtcNow,
                LastError = null
            },
            cancellationToken);

        return prefixes.Count;
    }

    public async Task<ReconciliationResult> EnableAsync(
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<string> prefixes =
            await EnsurePrefixesAsync(
                cancellationToken);

        DirectGateway gateway =
            _gatewayDetector.Detect();

        ReconciliationResult result =
            await _routeReconciler.EnableAsync(
                prefixes,
                gateway.Address,
                gateway.InterfaceIndex,
                metric: 5,
                cancellationToken);

        await _stateRepository.SaveAsync(
            new IranDirectState
            {
                Enabled = true,
                Gateway =
                    gateway.Address.ToString(),
                InterfaceIndex =
                    gateway.InterfaceIndex,
                InterfaceName =
                    gateway.InterfaceName,
                PrefixCount = prefixes.Count,
                EnabledAt =
                    DateTimeOffset.UtcNow,
                PrefixesUpdatedAt =
                    _prefixRepository.GetLastModified(),
                LastError = null
            },
            cancellationToken);

        return result;
    }

    public async Task<ReconciliationResult> DisableAsync(
        CancellationToken cancellationToken = default)
    {
        IranDirectState state =
            await _stateRepository.LoadAsync(
                cancellationToken);

        IReadOnlyList<string> prefixes =
            await _prefixRepository.LoadAsync(
                cancellationToken);

        if (!IPAddress.TryParse(
                state.Gateway,
                out IPAddress? gateway))
        {
            return new ReconciliationResult
            {
                DesiredCount = prefixes.Count
            };
        }

        ReconciliationResult result =
            await _routeReconciler.DisableAsync(
                prefixes,
                gateway,
                state.InterfaceIndex,
                cancellationToken);

        await _stateRepository.SaveAsync(
            state with
            {
                Enabled = false,
                LastError = null
            },
            cancellationToken);

        return result;
    }

    public async Task<IranDirectStatus> GetStatusAsync(
        CancellationToken cancellationToken = default)
    {
        IranDirectState state =
            await _stateRepository.LoadAsync(
                cancellationToken);

        IReadOnlyList<string> prefixes =
            await _prefixRepository.LoadAsync(
                cancellationToken);

        int installed = 0;

        if (IPAddress.TryParse(
                state.Gateway,
                out IPAddress? gateway)
            && state.InterfaceIndex > 0)
        {
            installed =
                await _routeReconciler
                    .CountMatchingRoutesAsync(
                        prefixes,
                        gateway,
                        state.InterfaceIndex,
                        cancellationToken);
        }

        return new IranDirectStatus
        {
            Enabled = state.Enabled,
            Gateway = state.Gateway,
            InterfaceIndex =
                state.InterfaceIndex,
            InterfaceName =
                state.InterfaceName,
            PrefixCount =
                prefixes.Count,
            InstalledRouteCount =
                installed,
            PrefixesUpdatedAt =
                state.PrefixesUpdatedAt,
            LastError =
                state.LastError
        };
    }

    public async Task RepairAsync(
        CancellationToken cancellationToken = default)
    {
        IranDirectState state =
            await _stateRepository.LoadAsync(
                cancellationToken);

        if (!state.Enabled)
        {
            return;
        }

        await EnableAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<string>>
        EnsurePrefixesAsync(
            CancellationToken cancellationToken)
    {
        IReadOnlyList<string> prefixes =
            await _prefixRepository.LoadAsync(
                cancellationToken);

        if (prefixes.Count > 0)
        {
            return prefixes;
        }

        await UpdatePrefixesAsync(
            cancellationToken);

        prefixes =
            await _prefixRepository.LoadAsync(
                cancellationToken);

        if (prefixes.Count == 0)
        {
            throw new InvalidOperationException(
                "No Iranian IPv4 prefixes are available.");
        }

        return prefixes;
    }
}