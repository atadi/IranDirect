using System.Collections.Concurrent;

namespace PathVeer.Core.Testing.FaultInjection;

/// <summary>
/// A fault policy that fails a given point EXACTLY ONCE, then reports success
/// for all subsequent calls. Used to prove that a transient read/access failure
/// is retried and recovered (rather than being misclassified as corruption).
/// Each instance is stateful and per-store, so it must not be shared across
/// concurrent operations on the same point.
/// </summary>
public sealed class OneShotFaultInjectionPolicy : IFaultInjectionPolicy
{
    private readonly ConcurrentDictionary<FaultInjectionPoint, bool> _remaining;

    public OneShotFaultInjectionPolicy(params FaultInjectionPoint[] points)
    {
        _remaining = new ConcurrentDictionary<FaultInjectionPoint, bool>(
            points.ToDictionary(p => p, _ => true));
    }

    public bool ShouldFail(FaultInjectionPoint point)
    {
        if (_remaining.TryGetValue(point, out bool present) && present)
        {
            _remaining[point] = false;
            return true;
        }

        return false;
    }
}
