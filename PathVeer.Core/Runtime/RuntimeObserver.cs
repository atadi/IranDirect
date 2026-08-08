namespace PathVeer.Core.Runtime;

public sealed class RuntimeObserver
{
    private readonly IRuntimeObservationSource _source;

    public RuntimeObserver(
        IRuntimeObservationSource source)
    {
        _source = source;
    }

    public async Task<ObservedRuntime> ObserveAsync(
        CancellationToken cancellationToken = default)
    {
        RuntimeObservationSourceResult<
            IReadOnlyList<ObservedVpnEndpoint>>
            endpointResult =
                await _source.ObserveVpnEndpointsAsync(
                    cancellationToken);

        RuntimeObservationSourceResult<
            ObservedDirectGateway>
            gatewayResult =
                _source.ObserveDirectGateway();

        IReadOnlyList<string> prefixes =
            await _source.ObservePrefixesAsync(
                cancellationToken);

        IReadOnlyList<ObservedRoute> routes =
            await _source.ObserveRoutesAsync(
                cancellationToken);

        return new ObservedRuntime
        {
            VpnProfileExists =
                _source.VpnProfileExists,
            VpnProfileValid =
                endpointResult.Succeeded,
            DirectGateway =
                gatewayResult.Succeeded
                    ? gatewayResult.Value
                    : null,
            VpnEndpoints =
                endpointResult.Value ?? [],
            Prefixes = prefixes,
            Routes = routes,
            ObservedAt = DateTimeOffset.UtcNow
        };
    }
}