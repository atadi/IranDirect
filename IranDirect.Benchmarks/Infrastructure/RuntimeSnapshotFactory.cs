using IranDirect.Core.Configuration;
using IranDirect.Core.Runtime;
using IranDirect.Core.Runtime.Execution;
using IranDirect.Core.Runtime.Reconciliation;
using IranDirect.Testing.Performance.Workloads;

namespace IranDirect.Benchmarks.Infrastructure;

public sealed record PlannerInput(
    RuntimePlanSnapshot Snapshot,
    RuntimeRouteOwnership Ownership);

public static class RuntimeSnapshotFactory
{
    public static PlannerInput CreatePlannerInput(RuntimeWorkload workload)
    {
        ArgumentNullException.ThrowIfNull(workload);

        var snapshot = new RuntimePlanSnapshot
        {
            Configuration = ConfigurationDefaults.Create() with
            {
                Enabled = true
            },
            Desired = new DesiredRuntime
            {
                Enabled = true,
                EndpointRoutes = workload.DesiredEndpointRoutes,
                PrefixRoutes = workload.DesiredPrefixes,
                Blockers = []
            },
            Observed = new ObservedRuntime
            {
                VpnProfileExists = true,
                VpnProfileValid = true,
                VpnEndpoints = workload.VpnEndpoints,
                Routes = workload.ObservedRoutes,
                ObservedAt = FixedTime.Value
            },
            PlannedAt = FixedTime.Value
        };

        var ownership = new RuntimeRouteOwnership
        {
            EndpointRouteIdentities = workload.VpnEndpointInventory.Endpoints
                .Where(item => item.AddedByIranDirect)
                .Select(item => item.Identity)
                .ToHashSet(StringComparer.OrdinalIgnoreCase),
            PrefixRouteIdentities = workload.RouteInventory.Routes
                .Select(item => item.Identity)
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
        };

        return new PlannerInput(snapshot, ownership);
    }

    public static RuntimeDecision CreateDecision(int stepCount)
    {
        if (stepCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(stepCount));
        }

        RuntimeWorkload workload = stepCount == 0
            ? new RuntimeWorkloadGenerator().Generate(
                RuntimeWorkloadScenario.AllPresent,
                RuntimeWorkloadSize.Scale1K)
            : new RuntimeWorkloadGenerator().Generate(
                RuntimeWorkloadScenario.AllMissing,
                (RuntimeWorkloadSize)stepCount);

        return CreateDecision(workload);
    }

    public static RuntimeDecision CreateDecision(RuntimeWorkload workload)
    {
        ArgumentNullException.ThrowIfNull(workload);

        PlannerInput input = CreatePlannerInput(workload);

        RuntimeChangeSet changes = new RuntimeChangeSetPlanner().Plan(
            input.Snapshot,
            input.Ownership);

        RuntimeReconciliationResult reconciliation = changes.IsEmpty
            ? RuntimeReconciliationResult.NoChanges()
            : RuntimeReconciliationResult.Planned(changes);

        RuntimeExecutionPlan executionPlan =
            new RuntimeExecutionPlanner().Plan(changes);

        return RuntimeDecision.Create(
            input.Snapshot,
            reconciliation,
            executionPlan,
            FixedTime.Value);
    }
}
