namespace PathVeer.Core.Runtime;

public interface IRuntimeDecisionBuilder
{
    Task<RuntimeDecision> BuildAsync(
        CancellationToken cancellationToken = default);
}
