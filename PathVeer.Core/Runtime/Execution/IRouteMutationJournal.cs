namespace PathVeer.Core.Runtime.Execution;

/// <summary>
/// Durable write-ahead intent record for native route mutations. The intent is
/// persisted BEFORE the native mutation and cleared AFTER the authoritative
/// inventory state is persisted, so that a crash between the two leaves a
/// recoverable proof that IranDirect initiated the operation.
/// </summary>
public interface IRouteMutationJournal
{
    /// <summary>Persist (or replace) the intent for <see cref="RouteMutationJournalEntry.RouteIdentity"/>.</summary>
    Task WriteIntentAsync(
        RouteMutationJournalEntry entry,
        CancellationToken cancellationToken = default);

    /// <summary>Remove the intent for the given route identity.</summary>
    Task ClearIntentAsync(
        string routeIdentity,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Load all outstanding intents. Throws <see cref="RouteMutationJournalCorruptException"/>
    /// if the file exists but is unreadable or has an unsupported schema version.
    /// Returns an empty map when no journal file exists.
    /// </summary>
    Task<IReadOnlyDictionary<string, RouteMutationJournalEntry>> LoadAllAsync(
        CancellationToken cancellationToken = default);

    /// <summary>Remove the journal file entirely (used after recovery completes).</summary>
    Task ClearAsync(
        CancellationToken cancellationToken = default);
}
