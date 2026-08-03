using IranDirect.Core.Prefixes;

namespace IranDirect.Core.Tests.Prefixes;

/// <summary>
/// Test-only reproduction of the pre-optimization
/// <see cref="PrefixDatasetComparer.Compare"/> behavior. It mirrors the
/// original implementation exactly (two <c>Normalize</c> passes feeding
/// case-insensitive <c>HashSet</c>s, two <c>Except(...).OrderBy(...)</c>
/// pipelines, and the <c>Count(Contains)</c> unchanged tally) so
/// equivalence tests can compare the optimized comparer against an
/// independent, behavior-faithful oracle. This type must never ship in
/// production code — it exists only to pin observable semantics during
/// the allocation optimization.
/// </summary>
public static class ReferencePrefixDatasetComparer
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
