namespace PathVeer.Testing.Performance.Persistence;

public sealed record PersistenceEndurancePlan
{
    public required PersistenceEnduranceScenario Scenario { get; init; }

    public required int Cycles { get; init; }

    public required int Seed { get; init; }

    public required int Concurrency { get; init; }

    public required string GeneratorVersion { get; init; }
}
