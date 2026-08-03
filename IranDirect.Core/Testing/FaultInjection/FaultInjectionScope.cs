using System.Collections.Immutable;

namespace IranDirect.Core.Testing.FaultInjection;

public sealed class FaultInjectionScope : IDisposable
{
    private static readonly AsyncLocal<FaultInjectionState?> Ambient =
        new();

    private readonly FaultInjectionState _state;
    private int _disposed;

    private FaultInjectionScope(FaultInjectionState state)
    {
        _state = state;
        Ambient.Value = state;
    }

    public static FaultInjectionScope Fail(
        params FaultInjectionPoint[] points)
    {
        ArgumentNullException.ThrowIfNull(points);

        FaultInjectionPolicy policy = FaultInjectionPolicy.For(points);

        return new FaultInjectionScope(
            new FaultInjectionState(policy, Ambient.Value));
    }

    public static ImmutableHashSet<FaultInjectionPoint> CurrentPoints =>
        Ambient.Value?.SelectedPoints()
        ?? ImmutableHashSet<FaultInjectionPoint>.Empty;

    public static bool IsActive => Ambient.Value is not null;

    public static bool ShouldFail(FaultInjectionPoint point) =>
        Ambient.Value?.Policy.ShouldFail(point) ?? false;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _state.Disposed = true;

        if (ReferenceEquals(Ambient.Value, _state))
        {
            Ambient.Value = FirstAlive(_state.Previous);
        }
    }

    private static FaultInjectionState? FirstAlive(
        FaultInjectionState? state)
    {
        FaultInjectionState? current = state;

        while (current is not null && current.Disposed)
        {
            current = current.Previous;
        }

        return current;
    }
}
