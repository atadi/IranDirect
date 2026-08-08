namespace PathVeer.Core.Runtime;

public sealed record DesiredRuntime
{
    public bool Enabled { get; init; }

    public IReadOnlyList<DesiredEndpointRoute> EndpointRoutes
        { get; init; } = [];

    public IReadOnlyList<DesiredPrefixRoute> PrefixRoutes
        { get; init; } = [];

    public IReadOnlyList<RuntimeBlocker> Blockers
        { get; init; } = [];

    public bool CanReconcile =>
        Blockers.Count == 0;
}