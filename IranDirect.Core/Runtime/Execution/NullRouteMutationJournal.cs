namespace IranDirect.Core.Runtime.Execution;

/// <summary>
/// No-op journal used when crash-consistency recovery is not wired (e.g. unit
/// tests and non-host callers that manage durability themselves). It makes the
/// journal integration invisible to existing callers while leaving the always-on
/// graceful compensation path unchanged.
/// </summary>
public sealed class NullRouteMutationJournal : IRouteMutationJournal
{
    public static readonly NullRouteMutationJournal Instance = new();

    private NullRouteMutationJournal()
    {
    }

    public Task WriteIntentAsync(
        RouteMutationJournalEntry entry,
        CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task ClearIntentAsync(
        string routeIdentity,
        CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task<IReadOnlyDictionary<string, RouteMutationJournalEntry>> LoadAllAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<string, RouteMutationJournalEntry>>(
            new Dictionary<string, RouteMutationJournalEntry>(
                StringComparer.OrdinalIgnoreCase));

    public Task ClearAsync(
        CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
