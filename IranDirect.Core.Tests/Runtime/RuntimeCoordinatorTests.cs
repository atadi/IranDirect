using IranDirect.Core.Configuration;
using IranDirect.Core.Runtime;

namespace IranDirect.Core.Tests.Runtime;

public sealed class RuntimeCoordinatorTests
{
    [Fact]
    public async Task BuildPlanAsync_ComposesConfigurationObservationAndPlan()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "IranDirect.Tests",
            Guid.NewGuid().ToString("N"),
            "desired-configuration.json");

        DesiredConfigurationValidator validator = new();

        DesiredConfigurationStore store = new(
            path,
            validator);

        await store.SaveAsync(
            ConfigurationDefaults.Create() with
            {
                Enabled = true
            });

        DesiredConfigurationService configurationService =
            new(store);

        RuntimeObserver observer =
            new(new HealthyObservationSource());

        RuntimePlanner planner = new(validator);

        RuntimeCoordinator coordinator = new(
            configurationService,
            observer,
            planner);

        RuntimePlanSnapshot snapshot =
            await coordinator.BuildPlanAsync();

        Assert.True(snapshot.Configuration.Enabled);
        Assert.True(snapshot.Observed.VpnProfileValid);
        Assert.True(snapshot.Desired.Enabled);
        Assert.True(snapshot.Desired.CanReconcile);
        Assert.Single(snapshot.Desired.EndpointRoutes);
        Assert.Single(snapshot.Desired.PrefixRoutes);
        Assert.NotEqual(default, snapshot.PlannedAt);
    }

    [Fact]
    public async Task BuildPlanAsync_WhenConfigurationDisabled_PlansNoPrefixRoutes()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "IranDirect.Tests",
            Guid.NewGuid().ToString("N"),
            "desired-configuration.json");

        DesiredConfigurationValidator validator = new();

        DesiredConfigurationService configurationService =
            new(
                new DesiredConfigurationStore(
                    path,
                    validator));

        RuntimeCoordinator coordinator = new(
            configurationService,
            new RuntimeObserver(
                new HealthyObservationSource()),
            new RuntimePlanner(validator));

        RuntimePlanSnapshot snapshot =
            await coordinator.BuildPlanAsync();

        Assert.False(snapshot.Configuration.Enabled);
        Assert.False(snapshot.Desired.Enabled);
        Assert.Empty(snapshot.Desired.PrefixRoutes);
        Assert.Single(snapshot.Desired.EndpointRoutes);
    }

    [Fact]
    public async Task BuildPlanAsync_WhenObservationIsBlocked_ReturnsBlockersWithoutExecuting()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "IranDirect.Tests",
            Guid.NewGuid().ToString("N"),
            "desired-configuration.json");

        DesiredConfigurationValidator validator = new();

        DesiredConfigurationStore store = new(
            path,
            validator);

        await store.SaveAsync(
            ConfigurationDefaults.Create() with
            {
                Enabled = true
            });

        RuntimeCoordinator coordinator = new(
            new DesiredConfigurationService(store),
            new RuntimeObserver(
                new MissingGatewayObservationSource()),
            new RuntimePlanner(validator));

        RuntimePlanSnapshot snapshot =
            await coordinator.BuildPlanAsync();

        Assert.False(snapshot.Desired.CanReconcile);
        Assert.Empty(snapshot.Desired.PrefixRoutes);
        Assert.Contains(
            snapshot.Desired.Blockers,
            blocker =>
                blocker.Code ==
                RuntimeBlockerCode.DirectGatewayUnavailable);
    }

    private class HealthyObservationSource :
        IRuntimeObservationSource
    {
        public bool VpnProfileExists => true;

        public Task<RuntimeObservationSourceResult<
            IReadOnlyList<ObservedVpnEndpoint>>>
            ObserveVpnEndpointsAsync(
                CancellationToken cancellationToken = default)
        {
            return Task.FromResult(
                RuntimeObservationSourceResult<
                    IReadOnlyList<ObservedVpnEndpoint>>.Success(
                    [
                        new ObservedVpnEndpoint
                        {
                            Host = "vpn.example",
                            Address = "5.160.74.148",
                            Port = 1409,
                            Protocol = "tcp"
                        }
                    ]));
        }

        public virtual RuntimeObservationSourceResult<
            ObservedDirectGateway>
            ObserveDirectGateway()
        {
            return RuntimeObservationSourceResult<
                ObservedDirectGateway>.Success(
                    new ObservedDirectGateway
                    {
                        Address = "192.168.100.1",
                        InterfaceIndex = 30,
                        InterfaceName = "Ethernet",
                        InterfaceMetric = 10
                    });
        }

        public Task<IReadOnlyList<string>>
            ObservePrefixesAsync(
                CancellationToken cancellationToken = default)
        {
            return Task.FromResult<
                IReadOnlyList<string>>(
                ["203.0.113.0/24"]);
        }

        public Task<IReadOnlyList<ObservedRoute>>
            ObserveRoutesAsync(
                CancellationToken cancellationToken = default)
        {
            return Task.FromResult<
                IReadOnlyList<ObservedRoute>>([]);
        }
    }

    private sealed class MissingGatewayObservationSource :
        HealthyObservationSource
    {
        public override RuntimeObservationSourceResult<
            ObservedDirectGateway>
            ObserveDirectGateway()
        {
            return RuntimeObservationSourceResult<
                ObservedDirectGateway>.Failure(
                    "No direct gateway is available.");
        }
    }
}