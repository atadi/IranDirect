namespace PathVeer.Core.Prefixes;

public static class PrefixDatasetComparer
{
    public static PrefixDatasetDiff Compare(
        IEnumerable<string> oldDataset,
        IEnumerable<string> newDataset)
    {
        ArgumentNullException.ThrowIfNull(oldDataset);
        ArgumentNullException.ThrowIfNull(newDataset);

        BuildSet(oldDataset, out HashSet<string> oldSet);
        BuildSet(newDataset, out HashSet<string> newSet);

        var added = new List<string>(oldSet.Count);
        var removed = new List<string>(oldSet.Count);
        int unchanged = 0;

        foreach (string prefix in newSet)
        {
            if (!oldSet.Contains(prefix))
            {
                added.Add(prefix);
            }
        }

        foreach (string prefix in oldSet)
        {
            if (!newSet.Contains(prefix))
            {
                removed.Add(prefix);
            }
            else
            {
                unchanged++;
            }
        }

        added.Sort(StringComparer.Ordinal);
        removed.Sort(StringComparer.Ordinal);

        bool hasChanges = added.Count > 0 || removed.Count > 0;

        return new PrefixDatasetDiff
        {
            AddedPrefixes = added,
            RemovedPrefixes = removed,
            UnchangedCount = unchanged,
            AddedCount = added.Count,
            RemovedCount = removed.Count,
            HasChanges = hasChanges
        };
    }

    private static void BuildSet(
        IEnumerable<string> prefixes,
        out HashSet<string> set)
    {
        set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string prefix in prefixes)
        {
            string normalized = prefix.Trim();
            if (normalized.Length > 0)
            {
                set.Add(normalized);
            }
        }
    }
}
