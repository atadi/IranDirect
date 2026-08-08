using PathVeer.Core.Configuration;

namespace PathVeer.Core.Runtime;

public sealed record RuntimePlanSnapshot
{
    public required DesiredConfiguration Configuration
        { get; init; }

    public required ObservedRuntime Observed
        { get; init; }

    public required DesiredRuntime Desired
        { get; init; }

    public DateTimeOffset PlannedAt { get; init; }
}