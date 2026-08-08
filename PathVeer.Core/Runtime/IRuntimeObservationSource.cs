namespace PathVeer.Core.Runtime;

public interface IRuntimeObservationSource
{
    bool VpnProfileExists { get; }

    Task<RuntimeObservationSourceResult<
        IReadOnlyList<ObservedVpnEndpoint>>>
        ObserveVpnEndpointsAsync(
            CancellationToken cancellationToken = default);

    RuntimeObservationSourceResult<ObservedDirectGateway>
        ObserveDirectGateway();

    Task<IReadOnlyList<string>> ObservePrefixesAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ObservedRoute>> ObserveRoutesAsync(
        CancellationToken cancellationToken = default);
}