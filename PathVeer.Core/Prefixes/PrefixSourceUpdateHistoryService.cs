using PathVeer.Core.Configuration;

namespace PathVeer.Core.Prefixes;

public sealed class PrefixSourceUpdateHistoryService :
    IPrefixSourceUpdateHistoryService
{
    private const int MaxErrorLength = 512;

    private readonly CountryPrefixStore _store;
    private readonly PrefixSourceHistoryOptions _options;
    private readonly TimeProvider _timeProvider;

    public PrefixSourceUpdateHistoryService(
        CountryPrefixStore store,
        PrefixSourceHistoryOptions? options = null,
        TimeProvider? timeProvider = null)
    {
        _store = store;
        _options = options ?? new PrefixSourceHistoryOptions();

        PrefixSourceHistoryOptions.Validate(_options);

        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<IReadOnlyList<PrefixSourceUpdateHistoryEntry>>
        GetRecentAsync(
            DirectCountryCode country,
            int? limit = null,
            CancellationToken cancellationToken = default)
    {
        int take = limit is null or < 1
            ? _options.RetentionCount
            : Math.Min(limit.Value, _options.RetentionCount);

        return await _store.GetUpdateHistoryRepository(country)
            .GetRecentAsync(take, cancellationToken);
    }

    public async Task RecordSuccessAsync(
        DirectCountryCode country,
        PrefixSourceFetchResult result,
        PrefixSourceChangeSummary? changeSummary = null,
        IReadOnlyList<string>? previousPrefixes = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(country);
        ArgumentNullException.ThrowIfNull(result);

        PrefixSourceDescriptor.Validate(result.Source);

        DateTimeOffset now = _timeProvider.GetUtcNow();

        PrefixSourceChangeSummary summary = changeSummary
            ?? BuildFallbackSummary(result, previousPrefixes);

        PrefixSourceUpdateHistoryEntry entry = new()
        {
            Id = Guid.NewGuid(),
            SourceId = result.Source.Id,
            SourceDisplayName = result.Source.DisplayName,
            SourceUri = result.Source.Uri,
            Format = result.Source.Format,
            ParserVersion = result.Source.ParserVersion,
            Status = PrefixSourceUpdateStatus.Succeeded,
            StartedAt = result.StartedAt,
            CompletedAt = result.CompletedAt,
            Duration = result.Duration,
            AttemptedAt = now,
            SourceLastModified = result.LastModified,
            ETag = result.ETag,
            PreviousContentHash = summary.PreviousContentHash,
            CurrentContentHash =
                summary.CurrentContentHash
                ?? result.ContentHash,
            ContentLength = result.ContentLength,
            PrefixCount = result.Prefixes.Count,
            AddedCount = summary.AddedCount,
            RemovedCount = summary.RemovedCount,
            UnchangedCount = summary.UnchangedCount,
            HasChanges = summary.HasChanges,
            Error = null
        };

        await _store.GetUpdateHistoryRepository(country)
            .AppendAsync(entry, cancellationToken);
    }

    public async Task RecordNotModifiedAsync(
        DirectCountryCode country,
        PrefixSourceFetchResult result,
        int currentPrefixCount = 0,
        string? currentContentHash = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(country);
        ArgumentNullException.ThrowIfNull(result);

        PrefixSourceDescriptor.Validate(result.Source);

        DateTimeOffset now = _timeProvider.GetUtcNow();

        PrefixSourceUpdateHistoryEntry entry = new()
        {
            Id = Guid.NewGuid(),
            SourceId = result.Source.Id,
            SourceDisplayName = result.Source.DisplayName,
            SourceUri = result.Source.Uri,
            Format = result.Source.Format,
            ParserVersion = result.Source.ParserVersion,
            Status = PrefixSourceUpdateStatus.NotModified,
            StartedAt = result.StartedAt,
            CompletedAt = result.CompletedAt,
            Duration = result.Duration,
            AttemptedAt = now,
            SourceLastModified = result.LastModified,
            ETag = result.ETag,
            PreviousContentHash = currentContentHash,
            CurrentContentHash = currentContentHash,
            ContentLength = null,
            PrefixCount = currentPrefixCount,
            AddedCount = 0,
            RemovedCount = 0,
            UnchangedCount = currentPrefixCount,
            HasChanges = false,
            Error = null
        };

        await _store.GetUpdateHistoryRepository(country)
            .AppendAsync(entry, cancellationToken);
    }

    public async Task RecordFailureAsync(
        DirectCountryCode country,
        PrefixSourceDescriptor source,
        string error,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(country);
        ArgumentNullException.ThrowIfNull(source);

        PrefixSourceDescriptor.Validate(source);

        string? conciseError = SanitizeError(error);

        if (conciseError is null)
        {
            throw new ArgumentException(
                "Prefix source update failure error must not be " +
                "empty.",
                nameof(error));
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();

        PrefixSourceUpdateHistoryEntry entry = new()
        {
            Id = Guid.NewGuid(),
            SourceId = source.Id,
            SourceDisplayName = source.DisplayName,
            SourceUri = source.Uri,
            Format = source.Format,
            ParserVersion = source.ParserVersion,
            Status = PrefixSourceUpdateStatus.Failed,
            StartedAt = now,
            CompletedAt = now,
            Duration = TimeSpan.Zero,
            AttemptedAt = now,
            SourceLastModified = null,
            ETag = null,
            PreviousContentHash = null,
            CurrentContentHash = null,
            ContentLength = null,
            PrefixCount = 0,
            AddedCount = 0,
            RemovedCount = 0,
            UnchangedCount = 0,
            HasChanges = false,
            Error = conciseError
        };

        await _store.GetUpdateHistoryRepository(country)
            .AppendAsync(entry, cancellationToken);
    }

    public async Task ClearAsync(
        DirectCountryCode country,
        CancellationToken cancellationToken = default) =>
        await _store.GetUpdateHistoryRepository(country)
            .ClearAsync(cancellationToken);

    private PrefixSourceChangeSummary BuildFallbackSummary(
        PrefixSourceFetchResult result,
        IReadOnlyList<string>? previousPrefixes)
    {
        if (previousPrefixes is null
            || previousPrefixes.Count == 0)
        {
            return new PrefixSourceChangeSummary
            {
                PreviousContentHash = null,
                CurrentContentHash = result.ContentHash,
                AddedCount = result.Prefixes.Count,
                RemovedCount = 0,
                UnchangedCount = 0,
                HasChanges = result.Prefixes.Count > 0,
                ComparedAt = _timeProvider.GetUtcNow()
            };
        }

        PrefixDatasetDiff diff = PrefixDatasetComparer.Compare(
            previousPrefixes,
            result.Prefixes);

        return new PrefixSourceChangeSummary
        {
            PreviousContentHash =
                PrefixContentHasher.ComputeHash(previousPrefixes),
            CurrentContentHash = result.ContentHash,
            AddedCount = diff.AddedCount,
            RemovedCount = diff.RemovedCount,
            UnchangedCount = diff.UnchangedCount,
            HasChanges = diff.HasChanges,
            ComparedAt = _timeProvider.GetUtcNow()
        };
    }

    private static string? SanitizeError(string? error)
    {
        if (string.IsNullOrWhiteSpace(error))
        {
            return null;
        }

        string collapsed = string.Join(
            " ",
            error!.Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries));

        return collapsed.Length <= MaxErrorLength
            ? collapsed
            : collapsed[..MaxErrorLength];
    }
}
