using IranDirect.Core.Prefixes;

namespace IranDirect.Core.Tests.Prefixes;

/// <summary>
/// Local mirror of the benchmark <c>DatasetScenario</c> enum and the
/// deterministic prefix generator, so the equivalence suite does not take
/// a dependency on the Benchmarks project. The values match the benchmark
/// factory producing identical datasets for the generated sweep.
/// </summary>
public enum LocalDatasetScenario
{
    Identical,
    AllAdded,
    AllRemoved,
    Mixed
}

internal static class LocalPrefixFactory
{
    public static (string[] Old, string[] New) Create(
        LocalDatasetScenario scenario,
        int size)
    {
        string[] Generate(int count)
        {
            var list = new List<string>(count);
            for (int i = 0; i < count; i++)
            {
                list.Add(ToCidr(i));
            }

            return list.ToArray();
        }

        string ToCidr(int index)
        {
            int first = index / 65_536 < 126
                ? index / 65_536 + 1
                : index / 65_536 + 2;
            int second = (index / 256) % 256;
            int third = index % 256;
            return $"{first}.{second}.{third}.0/24";
        }

        switch (scenario)
        {
            case LocalDatasetScenario.Identical:
            {
                string[] data = Generate(size);
                return (data, data);
            }

            case LocalDatasetScenario.AllAdded:
                return (Array.Empty<string>(), Generate(size));

            case LocalDatasetScenario.AllRemoved:
                return (Generate(size), Array.Empty<string>());

            case LocalDatasetScenario.Mixed:
            {
                string[] oldAll = Generate(size);
                int shared = size * 50 / 100;
                var additional = Generate(size * 2);
                string[] newData = oldAll
                    .Take(shared)
                    .Concat(additional.Skip(size).Take(size - shared))
                    .ToArray();
                return (oldAll, newData);
            }

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(scenario),
                    scenario,
                    null);
        }
    }
}

/// <summary>
/// Proves the optimized <see cref="PrefixDatasetComparer"/> produces
/// byte-for-byte equivalent results to the pre-optimization behavior
/// (modeled by <see cref="ReferencePrefixDatasetComparer"/>). Covers the
/// hand-picked semantics matrix from the optimization spec plus a
/// deterministic generated sweep across sizes 0..50K and the planned
/// scenarios, all seeded for reproducibility. No timing assertions are
/// used in correctness tests.
/// </summary>
public sealed class PrefixDatasetComparerEquivalenceTests
{
    [Fact]
    public void EmptyEmpty_NoChangesFromBoth()
    {
        PrefixDatasetDiff reference =
            ReferencePrefixDatasetComparer.Compare([], []);
        PrefixDatasetDiff optimized =
            PrefixDatasetComparer.Compare([], []);
        AssertEquivalent(reference, optimized);
    }

    [Fact]
    public void EmptyCurrent_AllAddedFromBoth()
    {
        string[] current = ["1.2.3.0/24", "10.0.0.0/8"];
        PrefixDatasetDiff reference =
            ReferencePrefixDatasetComparer.Compare([], current);
        PrefixDatasetDiff optimized =
            PrefixDatasetComparer.Compare([], current);
        AssertEquivalent(reference, optimized);
        Assert.Equal(2, optimized.AddedCount);
    }

    [Fact]
    public void PreviousEmpty_AllRemovedFromBoth()
    {
        string[] previous = ["1.2.3.0/24", "10.0.0.0/8"];
        PrefixDatasetDiff reference =
            ReferencePrefixDatasetComparer.Compare(previous, []);
        PrefixDatasetDiff optimized =
            PrefixDatasetComparer.Compare(previous, []);
        AssertEquivalent(reference, optimized);
        Assert.Equal(2, optimized.RemovedCount);
    }

    [Fact]
    public void Identical_NoChangesFromBoth()
    {
        string[] data = ["1.2.3.0/24", "10.0.0.0/8", "192.0.2.0/24"];
        PrefixDatasetDiff reference =
            ReferencePrefixDatasetComparer.Compare(data, data);
        PrefixDatasetDiff optimized =
            PrefixDatasetComparer.Compare(data, data);
        AssertEquivalent(reference, optimized);
    }

    [Fact]
    public void AllAdded_FromBoth()
    {
        PrefixDatasetDiff reference =
            ReferencePrefixDatasetComparer.Compare(
                ["1.2.3.0/24"],
                ["1.2.3.0/24", "10.0.0.0/8", "192.0.2.0/24"]);
        PrefixDatasetDiff optimized =
            PrefixDatasetComparer.Compare(
                ["1.2.3.0/24"],
                ["1.2.3.0/24", "10.0.0.0/8", "192.0.2.0/24"]);
        AssertEquivalent(reference, optimized);
    }

    [Fact]
    public void AllRemoved_FromBoth()
    {
        PrefixDatasetDiff reference =
            ReferencePrefixDatasetComparer.Compare(
                ["1.2.3.0/24", "10.0.0.0/8", "192.0.2.0/24"],
                ["1.2.3.0/24"]);
        PrefixDatasetDiff optimized =
            PrefixDatasetComparer.Compare(
                ["1.2.3.0/24", "10.0.0.0/8", "192.0.2.0/24"],
                ["1.2.3.0/24"]);
        AssertEquivalent(reference, optimized);
    }

    [Fact]
    public void Mixed_FromBoth()
    {
        PrefixDatasetDiff reference =
            ReferencePrefixDatasetComparer.Compare(
                ["1.2.3.0/24", "10.0.0.0/8"],
                ["10.0.0.0/8", "192.0.2.0/24"]);
        PrefixDatasetDiff optimized =
            PrefixDatasetComparer.Compare(
                ["1.2.3.0/24", "10.0.0.0/8"],
                ["10.0.0.0/8", "192.0.2.0/24"]);
        AssertEquivalent(reference, optimized);
    }

    [Fact]
    public void Duplicates_IgnoredFromBoth()
    {
        PrefixDatasetDiff reference =
            ReferencePrefixDatasetComparer.Compare(
                ["1.2.3.0/24", "1.2.3.0/24"],
                ["1.2.3.0/24", "10.0.0.0/8", "10.0.0.0/8"]);
        PrefixDatasetDiff optimized =
            PrefixDatasetComparer.Compare(
                ["1.2.3.0/24", "1.2.3.0/24"],
                ["1.2.3.0/24", "10.0.0.0/8", "10.0.0.0/8"]);
        AssertEquivalent(reference, optimized);
    }

    [Fact]
    public void BlankEntries_IgnoredFromBoth()
    {
        PrefixDatasetDiff reference =
            ReferencePrefixDatasetComparer.Compare(
                ["1.2.3.0/24", ""],
                ["1.2.3.0/24", "   "]);
        PrefixDatasetDiff optimized =
            PrefixDatasetComparer.Compare(
                ["1.2.3.0/24", ""],
                ["1.2.3.0/24", "   "]);
        AssertEquivalent(reference, optimized);
    }

    [Fact]
    public void WhitespaceTrimmed_FromBoth()
    {
        PrefixDatasetDiff reference =
            ReferencePrefixDatasetComparer.Compare(
                ["  1.2.3.0/24  "],
                ["1.2.3.0/24"]);
        PrefixDatasetDiff optimized =
            PrefixDatasetComparer.Compare(
                ["  1.2.3.0/24  "],
                ["1.2.3.0/24"]);
        AssertEquivalent(reference, optimized);
    }

    [Fact]
    public void CaseOnlyDifferences_TreatedAsUnchangedFromBoth()
    {
        PrefixDatasetDiff reference =
            ReferencePrefixDatasetComparer.Compare(
                ["ABC/24"],
                ["abc/24"]);
        PrefixDatasetDiff optimized =
            PrefixDatasetComparer.Compare(
                ["ABC/24"],
                ["abc/24"]);
        AssertEquivalent(reference, optimized);
        Assert.Equal(1, optimized.UnchangedCount);
    }

    [Fact]
    public void UnsortedInputs_IndependentOfOrderFromBoth()
    {
        PrefixDatasetDiff reference =
            ReferencePrefixDatasetComparer.Compare(
                ["1.2.3.0/24", "10.0.0.0/8"],
                ["10.0.0.0/8", "192.0.2.0/24"]);
        PrefixDatasetDiff optimized =
            PrefixDatasetComparer.Compare(
                ["10.0.0.0/8", "1.2.3.0/24"],
                ["192.0.2.0/24", "10.0.0.0/8"]);
        AssertEquivalent(reference, optimized);
    }

    [Fact]
    public void RepeatedValuesDifferentCasing_FromBoth()
    {
        PrefixDatasetDiff reference =
            ReferencePrefixDatasetComparer.Compare(
                ["ABC/24", "abc/24", "Abc/24"],
                ["abc/24", "ABD/24"]);
        PrefixDatasetDiff optimized =
            PrefixDatasetComparer.Compare(
                ["ABC/24", "abc/24", "Abc/24"],
                ["abc/24", "ABD/24"]);
        AssertEquivalent(reference, optimized);
    }

    // ---- Deterministic generated sweep (sizes 0..50K) ----

    public static IEnumerable<object[]> ScenarioSizeCases()
    {
        int[] sizes = [0, 1, 10, 100, 1_000, 10_000, 50_000];
        foreach (LocalDatasetScenario scenario in
                 Enum.GetValues<LocalDatasetScenario>())
        {
            foreach (int size in sizes)
            {
                yield return [scenario, size];
            }
        }
    }

    [Theory]
    [MemberData(nameof(ScenarioSizeCases))]
    public void OptimizedComparer_MatchesReference_Generated(
        LocalDatasetScenario scenario,
        int size)
    {
        (string[] oldData, string[] newData) =
            LocalPrefixFactory.Create(scenario, size);

        PrefixDatasetDiff reference =
            ReferencePrefixDatasetComparer.Compare(
                oldData,
                newData);
        PrefixDatasetDiff optimized =
            PrefixDatasetComparer.Compare(
                oldData,
                newData);

        AssertEquivalent(reference, optimized);
    }

    // ---- Deterministic synthetic scenarios (seeded) ----

    public static IEnumerable<object[]> SyntheticCases()
    {
        int[] sizes = [0, 1, 10, 100, 1_000, 10_000, 50_000];
        foreach (int size in sizes)
        {
            yield return ["duplicate-heavy", size];
            yield return ["whitespace-heavy", size];
            yield return ["case-variant-heavy", size];
        }
    }

    [Theory]
    [MemberData(nameof(SyntheticCases))]
    public void OptimizedComparer_MatchesReference_Synthetic(
        string scenario,
        int size)
    {
        (string[] oldData, string[] newData) = BuildSynthetic(
            scenario,
            size);

        PrefixDatasetDiff reference =
            ReferencePrefixDatasetComparer.Compare(
                oldData,
                newData);
        PrefixDatasetDiff optimized =
            PrefixDatasetComparer.Compare(
                oldData,
                newData);

        AssertEquivalent(reference, optimized);
    }

    private static (string[], string[]) BuildSynthetic(
        string scenario,
        int size)
    {
        var oldData = new List<string>(size);
        var newData = new List<string>(size);

        for (int i = 0; i < size; i++)
        {
            string basePrefix = $"{100 + (i % 120)}.{i % 256}.{i % 256}.0/24";

            string oldValue = scenario switch
            {
                "duplicate-heavy" => i % 2 == 0
                    ? basePrefix
                    : $"{basePrefix} ", // same after trim
                "whitespace-heavy" => i % 3 == 0
                    ? $"  {basePrefix}  "
                    : basePrefix,
                "case-variant-heavy" => i % 2 == 0
                    ? basePrefix
                    : basePrefix.ToUpperInvariant(),
                _ => basePrefix
            };

            string newValue = scenario switch
            {
                "duplicate-heavy" => i % 3 != 0
                    ? basePrefix
                    : $"{basePrefix}\t",
                "whitespace-heavy" => i % 5 == 0
                    ? $"\t{basePrefix}\n"
                    : basePrefix,
                "case-variant-heavy" => i % 2 != 0
                    ? basePrefix
                    : basePrefix.ToLowerInvariant(),
                _ => basePrefix
            };

            oldData.Add(oldValue);
            newData.Add(newValue);
        }

        return (oldData.ToArray(), newData.ToArray());
    }

    private static void AssertEquivalent(
        PrefixDatasetDiff expected,
        PrefixDatasetDiff actual)
    {
        Assert.Equal(expected.AddedCount, actual.AddedCount);
        Assert.Equal(expected.RemovedCount, actual.RemovedCount);
        Assert.Equal(
            expected.UnchangedCount,
            actual.UnchangedCount);
        Assert.Equal(expected.HasChanges, actual.HasChanges);
        Assert.Equal(expected.AddedPrefixes, actual.AddedPrefixes);
        Assert.Equal(expected.RemovedPrefixes, actual.RemovedPrefixes);
    }
}
