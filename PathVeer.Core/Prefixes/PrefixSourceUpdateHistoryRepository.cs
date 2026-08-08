namespace PathVeer.Core.Prefixes;

public sealed class PrefixSourceUpdateHistoryRepository :
    IPrefixSourceUpdateHistoryRepository
{
    private readonly PrefixSourceUpdateHistoryStore _store;
    private readonly PrefixSourceUpdateHistoryValidator _validator;
    private readonly PrefixSourceHistoryOptions _options;

    public PrefixSourceUpdateHistoryRepository(
        PrefixSourceUpdateHistoryStore store,
        PrefixSourceUpdateHistoryValidator validator,
        PrefixSourceHistoryOptions? options = null)
    {
        _store = store;
        _validator = validator;
        _options = options ?? new PrefixSourceHistoryOptions();

        PrefixSourceHistoryOptions.Validate(_options);
    }

    public async Task<PrefixSourceUpdateHistoryDocument> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        PrefixSourceUpdateHistoryDocument document =
            await _store.LoadAsync(cancellationToken);

        _validator.ValidateAndThrow(document);

        return document;
    }

    public async Task SaveAsync(
        PrefixSourceUpdateHistoryDocument document,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        _validator.ValidateAndThrow(document);

        await _store.SaveAsync(
            document,
            cancellationToken);
    }

    public async Task<IReadOnlyList<PrefixSourceUpdateHistoryEntry>>
        GetRecentAsync(
            int limit,
            CancellationToken cancellationToken = default)
    {
        if (limit <= 0)
        {
            return [];
        }

        PrefixSourceUpdateHistoryDocument document =
            await _store.LoadAsync(cancellationToken);

        IReadOnlyList<PrefixSourceUpdateHistoryEntry> entries =
            document.Entries ?? [];

        if (entries.Count == 0)
        {
            return [];
        }

        int take = Math.Min(limit, entries.Count);

        return entries
            .TakeLast(take)
            .Reverse()
            .ToArray();
    }

    public async Task AppendAsync(
        PrefixSourceUpdateHistoryEntry entry,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        _validator.ValidateAndThrow(entry);

        await _store.MutateAsync(
            document =>
            {
                List<PrefixSourceUpdateHistoryEntry> entries =
                    (document.Entries ?? []).ToList();

                entries.Add(entry);

                int retention = _options.RetentionCount;

                if (entries.Count > retention)
                {
                    entries.RemoveRange(
                        0,
                        entries.Count - retention);
                }

                return Task.FromResult<PrefixSourceUpdateHistoryDocument>(
                    document with { Entries = entries });
            },
            cancellationToken);
    }

    public async Task ClearAsync(
        CancellationToken cancellationToken = default)
    {
        await _store.MutateAsync(
            document => Task.FromResult(
                document with { Entries = [] }),
            cancellationToken);
    }
}
