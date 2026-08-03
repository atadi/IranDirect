using IranDirect.Core.Runtime.Execution;

namespace IranDirect.Testing.Performance.Execution;

public sealed class PerStepExecutionHandler : IRuntimeExecutionStepHandler
{
    private readonly ControlledRuntimeExecutionHandler _inner;

    public PerStepExecutionHandler(
        RuntimeExecutionConcurrencyProbe? probe = null,
        Func<RuntimeExecutionStep, CancellationToken, Task>? gate = null)
    {
        _inner = new ControlledRuntimeExecutionHandler(probe, gate);
    }

    public IReadOnlySet<string> FailIdentities
    {
        get => _inner.FailIdentities;
        set => _inner.FailIdentities = value;
    }

    public IReadOnlySet<string> CancelIdentities
    {
        get => _inner.CancelIdentities;
        set => _inner.CancelIdentities = value;
    }

    public IReadOnlySet<string> BenignRaceIdentities
    {
        get => _inner.BenignRaceIdentities;
        set => _inner.BenignRaceIdentities = value;
    }

    public int StepCallCount => _inner.StepCallCount;

    public int MutationCallCount => _inner.MutationCallCount;

    public int VerifyCallCount => _inner.VerifyCallCount;

    public IReadOnlyList<string> VerifiedIdentities => _inner.VerifiedIdentities;

    public Task<RuntimeExecutionStepResult> ExecuteAndVerifyAsync(
        RuntimeExecutionStep step,
        CancellationToken cancellationToken = default) =>
        _inner.ExecuteAndVerifyAsync(step, cancellationToken);
}
