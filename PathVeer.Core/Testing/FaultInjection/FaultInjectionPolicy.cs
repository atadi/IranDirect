using System.Collections.Immutable;

namespace PathVeer.Core.Testing.FaultInjection;

public sealed class FaultInjectionPolicy : IFaultInjectionPolicy
{
    private readonly ImmutableHashSet<FaultInjectionPoint> _points;

    private FaultInjectionPolicy(
        ImmutableHashSet<FaultInjectionPoint> points)
    {
        _points = points;
    }

    public static FaultInjectionPolicy Never { get; } =
        new(ImmutableHashSet<FaultInjectionPoint>.Empty);

    public static FaultInjectionPolicy For(
        IEnumerable<FaultInjectionPoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);

        return new FaultInjectionPolicy(points.ToImmutableHashSet());
    }

    public bool ShouldFail(FaultInjectionPoint point) =>
        _points.Contains(point);

    internal ImmutableHashSet<FaultInjectionPoint> Points => _points;
}
