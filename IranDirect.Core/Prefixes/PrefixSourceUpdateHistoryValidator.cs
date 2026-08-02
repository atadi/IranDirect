using System.Text.RegularExpressions;

namespace IranDirect.Core.Prefixes;

public sealed class PrefixSourceUpdateHistoryValidator
{
    private const int MaxErrorLength = 512;

    private static readonly Regex Sha256Hex =
        new("^[0-9a-f]{64}$", RegexOptions.Compiled);

    public void ValidateAndThrow(
        PrefixSourceUpdateHistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (string.IsNullOrWhiteSpace(entry.SourceId))
        {
            throw new InvalidOperationException(
                "Prefix source update history entry SourceId " +
                "must not be empty.");
        }

        if (string.IsNullOrWhiteSpace(entry.SourceDisplayName))
        {
            throw new InvalidOperationException(
                "Prefix source update history entry " +
                "SourceDisplayName must not be empty.");
        }

        if (entry.Status == PrefixSourceUpdateStatus.NeverUpdated)
        {
            throw new InvalidOperationException(
                "Prefix source update history entry Status must " +
                "not be NeverUpdated.");
        }

        if (entry.Status == PrefixSourceUpdateStatus.Failed
            && string.IsNullOrWhiteSpace(entry.Error))
        {
            throw new InvalidOperationException(
                "Prefix source update history failed entry must " +
                "include an error.");
        }

        if (entry.Status != PrefixSourceUpdateStatus.Failed
            && entry.Error is not null)
        {
            throw new InvalidOperationException(
                "Prefix source update history entry must not " +
                "include an error when not failed.");
        }

        if (entry.Error is { Length: > MaxErrorLength })
        {
            throw new InvalidOperationException(
                $"Prefix source update history entry Error must " +
                $"not exceed {MaxErrorLength} characters.");
        }

        if (entry.PrefixCount < 0
            || entry.AddedCount < 0
            || entry.RemovedCount < 0
            || entry.UnchangedCount < 0)
        {
            throw new InvalidOperationException(
                "Prefix source update history entry counts must " +
                "not be negative.");
        }

        if (entry.Duration < TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                "Prefix source update history entry Duration must " +
                "not be negative.");
        }

        if (entry.ContentLength is < 0)
        {
            throw new InvalidOperationException(
                "Prefix source update history entry ContentLength " +
                "must not be negative.");
        }

        if (entry.PreviousContentHash is not null
            && !Sha256Hex.IsMatch(entry.PreviousContentHash))
        {
            throw new InvalidOperationException(
                "Prefix source update history entry " +
                "PreviousContentHash must be a 64-character lowercase " +
                "SHA-256 hexadecimal value.");
        }

        if (entry.CurrentContentHash is not null
            && !Sha256Hex.IsMatch(entry.CurrentContentHash))
        {
            throw new InvalidOperationException(
                "Prefix source update history entry " +
                "CurrentContentHash must be a 64-character lowercase " +
                "SHA-256 hexadecimal value.");
        }

        if (entry.StartedAt == default)
        {
            throw new InvalidOperationException(
                "Prefix source update history entry StartedAt must " +
                "not be the default value.");
        }

        if (entry.CompletedAt == default)
        {
            throw new InvalidOperationException(
                "Prefix source update history entry CompletedAt " +
                "must not be the default value.");
        }

        if (entry.CompletedAt < entry.StartedAt)
        {
            throw new InvalidOperationException(
                "Prefix source update history entry CompletedAt " +
                "must not be earlier than StartedAt.");
        }

        if (entry.AttemptedAt == default)
        {
            throw new InvalidOperationException(
                "Prefix source update history entry AttemptedAt " +
                "must not be the default value.");
        }
    }

    public void ValidateAndThrow(
        PrefixSourceUpdateHistoryDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        foreach (PrefixSourceUpdateHistoryEntry entry
                 in document.Entries ?? [])
        {
            ValidateAndThrow(entry);
        }
    }
}
