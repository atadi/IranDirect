namespace IranDirect.Core.Prefixes;

public static class PrefixDatasetComparer
{
    public static PrefixDatasetDiff Compare(
        IEnumerable<string> oldDataset,
        IEnumerable<string> newDataset)
    {
        ArgumentNullException.ThrowIfNull(oldDataset);
        ArgumentNullException.ThrowIfNull(newDataset);

        HashSet<string> oldSet = new(
            Normalize(oldDataset),
            StringComparer.OrdinalIgnoreCase);
        HashSet<string> newSet = new(
            Normalize(newDataset),
            StringComparer.OrdinalIgnoreCase);

        IReadOnlyList<string> added = newSet
            .Except(oldSet, StringComparer.OrdinalIgnoreCase)
            .OrderBy(prefix => prefix, StringComparer.Ordinal)
            .ToArray();

        IReadOnlyList<string> removed = oldSet
            .Except(newSet, StringComparer.OrdinalIgnoreCase)
            .OrderBy(prefix => prefix, StringComparer.Ordinal)
            .ToArray();

        int unchanged = oldSet.Count(newSet.Contains);

        return new PrefixDatasetDiff
        {
            AddedPrefixes = added,
            RemovedPrefixes = removed,
            UnchangedCount = unchanged,
            AddedCount = added.Count,
            RemovedCount = removed.Count,
            HasChanges = added.Count > 0 || removed.Count > 0
        };
    }

    private static IEnumerable<string> Normalize(
        IEnumerable<string> prefixes) =>
        prefixes
            .Select(prefix => prefix.Trim())
            .Where(prefix => prefix.Length > 0);
}
