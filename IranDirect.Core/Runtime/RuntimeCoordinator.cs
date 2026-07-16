using IranDirect.Core.Configuration;

namespace IranDirect.Core.Runtime;

public sealed class RuntimeCoordinator
{
    private readonly DesiredConfigurationService
        _configurationService;
    private readonly RuntimeObserver _observer;
    private readonly RuntimePlanner _planner;

    public RuntimeCoordinator(
        DesiredConfigurationService configurationService,
        RuntimeObserver observer,
        RuntimePlanner planner)
    {
        _configurationService = configurationService;
        _observer = observer;
        _planner = planner;
    }

    public async Task<RuntimePlanSnapshot> BuildPlanAsync(
        CancellationToken cancellationToken = default)
    {
        DesiredConfiguration configuration =
            await _configurationService.GetAsync(
                cancellationToken);

        ObservedRuntime observed =
            await _observer.ObserveAsync(
                cancellationToken);

        DesiredRuntime desired =
            _planner.Plan(
                configuration,
                observed);

        return new RuntimePlanSnapshot
        {
            Configuration = configuration,
            Observed = observed,
            Desired = desired,
            PlannedAt = DateTimeOffset.UtcNow
        };
    }
}