using IranDirect.Core.Runtime;

namespace IranDirect.Testing.Performance.Lifecycle;

/// <summary>
/// The mutable observation view the simulation drives the runtime
/// with. The fake observation source reads this state, so each cycle
/// re-observes whatever the simulation has applied (including routes
/// the previous cycle installed through the execution handler).
/// </summary>
public sealed class ObservationState
{
    public bool VpnProfileExists { get; set; } = true;

    public ObservedDirectGateway? DirectGateway { get; set; }

    public IReadOnlyList<ObservedVpnEndpoint> VpnEndpoints
        { get; set; } = [];

    public IReadOnlyList<string> Prefixes { get; set; } = [];

    public IReadOnlyList<ObservedRoute> Routes { get; set; } = [];

    public ObservationState Clone() =>
        new()
        {
            VpnProfileExists = VpnProfileExists,
            DirectGateway = DirectGateway,
            VpnEndpoints = VpnEndpoints,
            Prefixes = Prefixes,
            Routes = Routes
        };
}
