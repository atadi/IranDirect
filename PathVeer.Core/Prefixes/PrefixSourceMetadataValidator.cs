using System.Text.RegularExpressions;

namespace PathVeer.Core.Prefixes;

public sealed class PrefixSourceMetadataValidator
{
    private const int MaxErrorLength = 500;

    private static readonly Regex Sha256Hex =
        new("^[0-9a-f]{64}$", RegexOptions.Compiled);

    public void ValidateAndThrow(
        PrefixSourceMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        if (string.IsNullOrWhiteSpace(metadata.SourceId))
        {
            throw new InvalidOperationException(
                "Prefix source metadata SourceId must not be empty.");
        }

        if (string.IsNullOrWhiteSpace(metadata.SourceDisplayName))
        {
            throw new InvalidOperationException(
                "Prefix source metadata SourceDisplayName must not be empty.");
        }

        if (metadata.PrefixCount < 0)
        {
            throw new InvalidOperationException(
                "Prefix source metadata PrefixCount must not be negative.");
        }

        if (metadata.DownloadDuration is { } duration
            && duration < TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                "Prefix source metadata DownloadDuration must not be negative.");
        }

        if (metadata.ContentLength is < 0)
        {
            throw new InvalidOperationException(
                "Prefix source metadata ContentLength must not be negative.");
        }

        if (metadata.ContentHash is not null
            && !Sha256Hex.IsMatch(metadata.ContentHash))
        {
            throw new InvalidOperationException(
                "Prefix source metadata ContentHash must be a " +
                "64-character lowercase SHA-256 hexadecimal value.");
        }

        if (metadata.LastSucceededAt is { } succeededAt
            && metadata.LastAttemptedAt < succeededAt)
        {
            throw new InvalidOperationException(
                "Prefix source metadata LastSucceededAt must not be " +
                "later than LastAttemptedAt.");
        }

        if (metadata.LastError is { Length: > MaxErrorLength })
        {
            throw new InvalidOperationException(
                $"Prefix source metadata LastError must not exceed " +
                $"{MaxErrorLength} characters.");
        }

        if (metadata.ChangeSummary is { } summary)
        {
            ValidateChangeSummary(summary);
        }
    }

    public void ValidateAndThrow(
        PrefixSourceMetadataDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (document.Current is { } current)
        {
            ValidateAndThrow(current);
        }
    }

    private static void ValidateChangeSummary(
        PrefixSourceChangeSummary summary)
    {
        if (summary.AddedCount < 0
            || summary.RemovedCount < 0
            || summary.UnchangedCount < 0)
        {
            throw new InvalidOperationException(
                "Prefix source change summary counts must not be negative.");
        }

        if (summary.PreviousContentHash is not null
            && !Sha256Hex.IsMatch(summary.PreviousContentHash))
        {
            throw new InvalidOperationException(
                "Prefix source change summary PreviousContentHash must be a " +
                "64-character lowercase SHA-256 hexadecimal value.");
        }

        if (summary.CurrentContentHash is not null
            && !Sha256Hex.IsMatch(summary.CurrentContentHash))
        {
            throw new InvalidOperationException(
                "Prefix source change summary CurrentContentHash must be a " +
                "64-character lowercase SHA-256 hexadecimal value.");
        }

        if (summary.ComparedAt == default)
        {
            throw new InvalidOperationException(
                "Prefix source change summary ComparedAt must not be the " +
                "default value.");
        }
    }
}
