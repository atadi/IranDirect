namespace PathVeer.Core.Runtime.Execution;

/// <summary>
/// The kind of native route mutation a journal entry describes.
/// </summary>
public enum RouteMutationKind
{
    Add = 0,
    Delete = 1
}

/// <summary>
/// Which ownership inventory a journal entry concerns, so recovery knows
/// which store to reconcile after a crash.
/// </summary>
public enum RouteMutationInventoryKind
{
    Prefix = 0,
    Endpoint = 1
}

/// <summary>
/// A durable, write-ahead proof that IranDirect initiated a native route
/// mutation. It is persisted BEFORE the native mutation so that recovery after
/// a crash can distinguish an IranDirect-owned route from an externally created
/// one that merely resembles a desired route.
///
/// The journal intentionally carries only the bounded identity fields needed to
/// recover ownership; it never holds command output, exception text, or secrets.
/// </summary>
public sealed record RouteMutationJournalEntry
{
    public int SchemaVersion { get; init; } = RouteMutationJournal.SchemaVersion;

    public RouteMutationKind Kind { get; init; }

    public RouteMutationInventoryKind InventoryKind { get; init; }

    public string RouteIdentity { get; init; } = "";

    public string DestinationPrefix { get; init; } = "";

    public string Gateway { get; init; } = "";

    public uint InterfaceIndex { get; init; }

    public int Metric { get; init; }

    /// <summary>
    /// VPN endpoint host for endpoint routes; unused for prefix routes.
    /// Carried so recovery can reconstruct an endpoint inventory item.
    /// </summary>
    public string Description { get; init; } = "";

    public Guid MutationId { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>
/// The on-disk shape of the journal: a schema version plus a map keyed by
/// route identity. At most one intent per route identity is retained, because
/// reconciliation will not issue both an add and a delete for the same identity
/// within a single plan and the executor serializes through the journal lock.
/// </summary>
public sealed record RouteMutationJournalFile
{
    public int SchemaVersion { get; init; } = RouteMutationJournal.SchemaVersion;

    public Dictionary<string, RouteMutationJournalEntry> Entries { get; init; }
        = new Dictionary<string, RouteMutationJournalEntry>(
            StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Thrown when the recovery journal file exists but cannot be parsed or carries
/// an unsupported schema version. Recovery must treat this as a hard stop: it
/// must not infer ownership from native route shape, must not mutate routes,
/// and must surface the condition deterministically.
/// </summary>
public sealed class RouteMutationJournalCorruptException : Exception
{
    public RouteMutationJournalCorruptException(string message)
        : base(message)
    {
    }

    public RouteMutationJournalCorruptException(
        string message,
        Exception inner)
        : base(message, inner)
    {
    }
}

public static class RouteMutationJournal
{
    public const int SchemaVersion = 1;
}
