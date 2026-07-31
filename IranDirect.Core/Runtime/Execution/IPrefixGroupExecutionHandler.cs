namespace IranDirect.Core.Runtime.Execution;

/// <summary>
/// Executes the prefix-route portions of a runtime plan in two phases:
/// a bounded-concurrency mutation phase and a group verification phase
/// that classifies every attempted step against a single authoritative
/// route-table snapshot.
/// </summary>
/// <remarks>
/// Implementations must be mutation-only during
/// <see cref="MutatePrefixRouteAsync"/> (no full route-table reads) and
/// must not write inventory ownership. Ownership is established by
/// <see cref="VerifyPrefixRouteGroupAsync"/> after the group snapshot.
/// </remarks>
public interface IPrefixGroupExecutionHandler
{
    /// <summary>
    /// Applies the route add or delete represented by
    /// <paramref name="step"/> without reading the route table and
    /// without writing inventory. Returned failures include benign races
    /// (already-exists adds, not-found deletes) that are resolved by the
    /// subsequent group verification.
    /// </summary>
    Task<PrefixMutationResult> MutatePrefixRouteAsync(
        RuntimeExecutionStep step,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a single route-table snapshot and classifies every step in
    /// <paramref name="steps"/> (which must be a homogeneous group of
    /// add or remove prefix steps) as Succeeded or Failed. Per-step
    /// inventory ownership is written only for steps whose postcondition
    /// was verified against the snapshot. Results are returned in the
    /// same order as <paramref name="steps"/>.
    /// </summary>
    Task<IReadOnlyList<RuntimeExecutionStepResult>> VerifyPrefixRouteGroupAsync(
        IReadOnlyList<RuntimeExecutionStep> steps,
        IReadOnlyList<PrefixMutationResult> mutationResults,
        CancellationToken cancellationToken = default);
}
