using IranDirect.Core.Runtime;

namespace IranDirect.Testing.Performance.Lifecycle;

/// <summary>
/// Deterministic <see cref="IRuntimeObservationSource"/> backed by an
/// <see cref="ObservationState"/>. Always succeeds; the harness
/// simulates observation/route failures at the route-manager fault
/// points instead, which keeps the production observer unchanged.
/// </summary>
public sealed class FakeObservationSource : IRuntimeObservationSource
{
    private readonly ObservationState _state;

    public FakeObservationSource(ObservationState state)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
    }

    public bool VpnProfileExists => _state.VpnProfileExists;

    public Task<RuntimeObservationSourceResult<
        IReadOnlyList<ObservedVpnEndpoint>>>
        ObserveVpnEndpointsAsync(
            CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(
            RuntimeObservationSourceResult<
                IReadOnlyList<ObservedVpnEndpoint>>
                .Success(_state.VpnEndpoints));
    }

    public RuntimeObservationSourceResult<ObservedDirectGateway>
        ObserveDirectGateway() =>
        _state.DirectGateway is null
            ? RuntimeObservationSourceResult<
                ObservedDirectGateway>.Failure(
                    "No direct gateway observed.")
            : RuntimeObservationSourceResult<
                ObservedDirectGateway>.Success(
                    _state.DirectGateway);

    public Task<IReadOnlyList<string>> ObservePrefixesAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_state.Prefixes);
    }

    public Task<IReadOnlyList<ObservedRoute>> ObserveRoutesAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<ObservedRoute> routes = _state.Routes;

        return Task.FromResult(routes);
    }
}
