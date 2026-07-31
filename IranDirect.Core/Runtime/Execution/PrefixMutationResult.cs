namespace IranDirect.Core.Runtime.Execution;

/// <summary>
/// Describes the outcome of a single prefix-route mutation performed by
/// <see cref="IPrefixGroupExecutionHandler.MutatePrefixRouteAsync"/>.
/// A failed mutation is not a final step verdict: the executor resolves
/// the final per-step status from the group verification snapshot, which
/// accounts for benign races such as add-returns-already-exists and
/// remove-returns-not-found.
/// </summary>
public sealed record PrefixMutationResult
{
    public bool Succeeded { get; init; }

    public string? ErrorMessage { get; init; }

    public static PrefixMutationResult Success() =>
        new() { Succeeded = true };

    public static PrefixMutationResult Failure(string? errorMessage) =>
        new() { Succeeded = false, ErrorMessage = errorMessage };
}
