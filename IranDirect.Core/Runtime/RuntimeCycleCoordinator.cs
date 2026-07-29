namespace IranDirect.Core.Runtime;

public sealed class RuntimeCycleCoordinator
{
    private readonly IRuntimeDecisionBuilder _decisionBuilder;

    public RuntimeCycleCoordinator(
        IRuntimeDecisionBuilder decisionBuilder)
    {
        ArgumentNullException.ThrowIfNull(decisionBuilder);

        _decisionBuilder = decisionBuilder;
    }

    public Task<RuntimeDecision> RunCycleAsync(
        CancellationToken cancellationToken = default)
    {
        return _decisionBuilder.BuildAsync(
            cancellationToken);
    }
}
