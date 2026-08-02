namespace IranDirect.Core.Prefixes;

public sealed class PrefixSourceMetadataService :
    IPrefixSourceMetadataService
{
    private const int MaxErrorLength = 500;

    private readonly IPrefixSourceMetadataRepository
        _repository;
    private readonly TimeProvider _timeProvider;

    public PrefixSourceMetadataService(
        IPrefixSourceMetadataRepository repository,
        TimeProvider? timeProvider = null)
    {
        _repository = repository;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<PrefixSourceMetadata?> GetCurrentAsync(
        CancellationToken cancellationToken = default)
    {
        PrefixSourceMetadataDocument document =
            await _repository.LoadAsync(
                cancellationToken);

        return document.Current;
    }

    public async Task<PrefixSourceChangeSummary?>
        GetLatestChangeSummaryAsync(
            CancellationToken cancellationToken = default)
    {
        PrefixSourceMetadata? metadata =
            await GetCurrentAsync(cancellationToken);

        return metadata?.ChangeSummary;
    }

    public async Task RecordSuccessAsync(
        PrefixSourceFetchResult result,
        IReadOnlyList<string>? previousPrefixes = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);

        PrefixSourceDescriptor.Validate(result.Source);

        DateTimeOffset now = _timeProvider.GetUtcNow();

        PrefixSourceMetadataDocument document =
            await _repository.LoadAsync(
                cancellationToken);

        PrefixSourceChangeSummary? changeSummary =
            BuildChangeSummary(
                document.Current,
                result,
                previousPrefixes,
                now);

        PrefixSourceMetadata metadata = new()
        {
            SourceId = result.Source.Id,
            SourceDisplayName = result.Source.DisplayName,
            SourceUri = result.Source.Uri,
            Format = result.Source.Format,
            ParserVersion = result.Source.ParserVersion,
            LastAttemptedAt = now,
            LastSucceededAt = now,
            SourceLastModified = result.LastModified,
            ETag = result.ETag,
            ContentHash = result.ContentHash,
            ContentLength = result.ContentLength,
            PrefixCount = result.Prefixes.Count,
            DownloadDuration = result.Duration,
            LastStatus = PrefixSourceUpdateStatus.Succeeded,
            LastError = null,
            ChangeSummary = changeSummary
        };

        await _repository.SaveAsync(
            document with { Current = metadata },
            cancellationToken);
    }

    public async Task RecordNotModifiedAsync(
        PrefixSourceFetchResult result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);

        PrefixSourceDescriptor.Validate(result.Source);

        DateTimeOffset now = _timeProvider.GetUtcNow();

        PrefixSourceMetadataDocument document =
            await _repository.LoadAsync(
                cancellationToken);

        PrefixSourceMetadata updated = WithSource(
                result.Source,
                document.Current ?? CreateEmpty())
            with
            {
                LastAttemptedAt = now,
                SourceLastModified = result.LastModified
                    ?? document.Current?.SourceLastModified,
                ETag = result.ETag
                    ?? document.Current?.ETag,
                LastStatus =
                    PrefixSourceUpdateStatus.NotModified,
                LastError = null
            };

        await _repository.SaveAsync(
            document with { Current = updated },
            cancellationToken);
    }

    public async Task RecordFailureAsync(
        PrefixSourceDescriptor source,
        string error,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        PrefixSourceDescriptor.Validate(source);

        DateTimeOffset now = _timeProvider.GetUtcNow();

        string? conciseError = Truncate(error);

        PrefixSourceMetadataDocument document =
            await _repository.LoadAsync(
                cancellationToken);

        PrefixSourceMetadata updated = WithSource(
                source,
                document.Current ?? CreateEmpty())
            with
            {
                LastAttemptedAt = now,
                LastStatus = PrefixSourceUpdateStatus.Failed,
                LastError = conciseError
            };

        await _repository.SaveAsync(
            document with { Current = updated },
            cancellationToken);
    }

    private static PrefixSourceMetadata CreateEmpty() =>
        new();

    private static PrefixSourceChangeSummary? BuildChangeSummary(
        PrefixSourceMetadata? previous,
        PrefixSourceFetchResult result,
        IReadOnlyList<string>? previousPrefixes,
        DateTimeOffset comparedAt)
    {
        string? previousHash = previous?.ContentHash;
        string? currentHash = result.ContentHash;
        int currentCount = result.Prefixes.Count;

        if (previous is null)
        {
            return new PrefixSourceChangeSummary
            {
                PreviousContentHash = previousHash,
                CurrentContentHash = currentHash,
                AddedCount = currentCount,
                RemovedCount = 0,
                UnchangedCount = 0,
                HasChanges = true,
                ComparedAt = comparedAt
            };
        }

        if (previousHash is not null
            && currentHash is not null
            && string.Equals(
                previousHash,
                currentHash,
                StringComparison.Ordinal))
        {
            return new PrefixSourceChangeSummary
            {
                PreviousContentHash = previousHash,
                CurrentContentHash = currentHash,
                AddedCount = 0,
                RemovedCount = 0,
                UnchangedCount = currentCount,
                HasChanges = false,
                ComparedAt = comparedAt
            };
        }

        PrefixDatasetDiff diff = PrefixDatasetComparer.Compare(
            previousPrefixes ?? [],
            result.Prefixes);

        return new PrefixSourceChangeSummary
        {
            PreviousContentHash = previousHash,
            CurrentContentHash = currentHash,
            AddedCount = diff.AddedCount,
            RemovedCount = diff.RemovedCount,
            UnchangedCount = diff.UnchangedCount,
            HasChanges = diff.HasChanges,
            ComparedAt = comparedAt
        };
    }

    public static string? FormatChangeSummary(
        PrefixSourceChangeSummary? summary)
    {
        if (summary is null)
        {
            return null;
        }

        return summary.HasChanges
            ? $"+{summary.AddedCount} -{summary.RemovedCount} " +
              $"unchanged {summary.UnchangedCount}"
            : $"No changes ({summary.UnchangedCount} prefixes).";
    }

    private static PrefixSourceMetadata WithSource(
        PrefixSourceDescriptor source,
        PrefixSourceMetadata prior) =>
        prior with
        {
            SourceId = source.Id,
            SourceDisplayName = source.DisplayName,
            SourceUri = source.Uri,
            Format = source.Format,
            ParserVersion = source.ParserVersion
        };

    private static string? Truncate(string? error)
    {
        if (string.IsNullOrWhiteSpace(error))
        {
            return null;
        }

        return error!.Length <= MaxErrorLength
            ? error
            : error[..MaxErrorLength];
    }
}
