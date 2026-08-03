using IranDirect.Testing.Performance.Workloads;

namespace IranDirect.Benchmarks.Infrastructure;

public sealed record PrefixDatasetPair(
    IReadOnlyList<string> OldDataset,
    IReadOnlyList<string> NewDataset);

public static class PrefixDatasetFactory
{
    public static PrefixDatasetPair Create(
        DatasetScenario scenario,
        int size)
    {
        var generator = new PrefixWorkloadGenerator();

        return scenario switch
        {
            DatasetScenario.Identical =>
                new PrefixDatasetPair(
                    generator.Generate(size),
                    generator.Generate(size)),
            DatasetScenario.AllAdded =>
                new PrefixDatasetPair(
                    Array.Empty<string>(),
                    generator.Generate(size)),
            DatasetScenario.AllRemoved =>
                new PrefixDatasetPair(
                    generator.Generate(size),
                    Array.Empty<string>()),
            DatasetScenario.Mixed =>
                CreateMixed(generator, size),
            _ => throw new ArgumentOutOfRangeException(
                nameof(scenario),
                scenario,
                null)
        };
    }

    private static PrefixDatasetPair CreateMixed(
        PrefixWorkloadGenerator generator,
        int size)
    {
        int shared = size * 50 / 100;

        IReadOnlyList<string> oldAll = generator.Generate(size);
        IReadOnlyList<string> additional = generator.Generate(size * 2);

        string[] newDataset = oldAll
            .Take(shared)
            .Concat(additional.Skip(size).Take(size - shared))
            .ToArray();

        return new PrefixDatasetPair(oldAll, newDataset);
    }
}
