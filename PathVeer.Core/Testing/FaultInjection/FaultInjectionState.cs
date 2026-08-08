using System.Collections.Immutable;

namespace PathVeer.Core.Testing.FaultInjection;

internal sealed class FaultInjectionState
{
    public FaultInjectionState(
        IFaultInjectionPolicy policy,
        FaultInjectionState? previous)
    {
        Policy = policy;
        Previous = previous;
    }

    public IFaultInjectionPolicy Policy { get; }

    public FaultInjectionState? Previous { get; }

    public bool Disposed { get; set; }

    public ImmutableHashSet<FaultInjectionPoint> SelectedPoints()
    {
        if (Policy is FaultInjectionPolicy scopedPolicy)
        {
            return scopedPolicy.Points;
        }

        return ImmutableHashSet<FaultInjectionPoint>.Empty;
    }
}
