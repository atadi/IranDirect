namespace IranDirect.Core.Tests.Runtime.Execution;

using System.Net;
using IranDirect.Core.Runtime.Execution;
using IranDirect.Core.Routing;
using IranDirect.Core.SystemTools;
using IranDirect.Core.Testing.FaultInjection;
using IranDirect.Core.Vpn;

public sealed class WindowsRouteManagerRuntimeFaultInjectionTests
{
    private static readonly RuntimeExecutionStep AddEndpointStep = new()
    {
        Kind = RuntimeExecutionStepKind.AddEndpointRoute,
        Identity = "10.0.0.1/32|192.168.1.1|10",
        DestinationPrefix = "10.0.0.1/32",
        Gateway = "192.168.1.1",
        InterfaceIndex = 10,
        Metric = 1,
        Description = "vpn.example.com"
    };

    private static readonly RuntimeExecutionStep RemoveEndpointStep =
        AddEndpointStep with
        {
            Kind = RuntimeExecutionStepKind.RemoveEndpointRoute,
            Description = "Remove test endpoint."
        };

    private static readonly RuntimeExecutionStep AddPrefixStep = new()
    {
        Kind = RuntimeExecutionStepKind.AddPrefixRoute,
        Identity = "203.0.113.0/24|192.168.1.1|10",
        DestinationPrefix = "203.0.113.0/24",
        Gateway = "192.168.1.1",
        InterfaceIndex = 10,
        Metric = 256,
        Description = "Add test prefix."
    };

    private static readonly RuntimeExecutionStep RemovePrefixStep =
        AddPrefixStep with
        {
            Kind = RuntimeExecutionStepKind.RemovePrefixRoute,
            Description = "Remove test prefix."
        };

    [Fact]
    public async Task RouteCreateFault_EndpointAdd_StepFailed_NoInventoryMutation_NoCompensation()
    {
        ExecutorContext context = CreateExecutor(
            FaultInjectionPolicy.For(
                [FaultInjectionPoint.RouteCreate]));
        RuntimeExecutionPlan plan = new()
        {
            Steps = [AddEndpointStep]
        };

        RuntimeExecutionResult result =
            await context.Executor.ExecuteAsync(plan);

        Assert.Equal(
            RuntimeExecutionResultStatus.Failed,
            result.Status);
        Assert.False(result.MutatedInfrastructure);

        RuntimeExecutionStepResult step =
            Assert.Single(result.StepResults);
        Assert.Equal(
            RuntimeExecutionStepStatus.Failed,
            step.Status);
        Assert.Contains(
            "Fault injected at RouteCreate",
            step.ErrorMessage);

        Assert.Equal(0, context.Api.AddCallCount);
        Assert.Equal(0, context.Api.DeleteCallCount);
        Assert.Empty(
            (await context.EndpointInventory.LoadAsync()).Endpoints);
    }

    [Fact]
    public async Task RouteCreateFault_SequentialGroup_StopsAndSkipsLaterSteps_ProgressDeterministic()
    {
        ExecutorContext context = CreateExecutor(
            FaultInjectionPolicy.For(
                [FaultInjectionPoint.RouteCreate]));
        RuntimeExecutionPlan plan = new()
        {
            Steps = [AddEndpointStep, AddEndpointStep]
        };
        List<RuntimeExecutionProgress> progress = [];

        RuntimeExecutionResult result =
            await context.Executor.ExecuteAsync(
                plan,
                new CollectingProgress(progress));

        Assert.Equal(
            RuntimeExecutionResultStatus.Failed,
            result.Status);
        Assert.Equal(
            RuntimeExecutionStepStatus.Failed,
            result.StepResults[0].Status);
        Assert.Equal(
            RuntimeExecutionStepStatus.Skipped,
            result.StepResults[1].Status);
        Assert.Equal(0, context.Api.AddCallCount);

        RuntimeExecutionProgress last = progress[^1];
        Assert.Equal(2, last.TotalSteps);
        Assert.Equal(2, last.ProcessedSteps);
        Assert.Equal(0, last.SucceededSteps);
        Assert.Equal(1, last.FailedSteps);
        Assert.Equal(0, last.CancelledSteps);
        Assert.Equal(1, last.SkippedSteps);
    }

    [Fact]
    public async Task RouteCreateFault_PrefixGroup_AllFailed_InventoryEmpty_NoNativeAdds()
    {
        ExecutorContext context = CreateExecutor(
            FaultInjectionPolicy.For(
                [FaultInjectionPoint.RouteCreate]));
        RuntimeExecutionPlan plan = new()
        {
            Steps =
            [
                AddPrefixStep,
                AddPrefixStep with
                {
                    Identity = "203.0.113.1/24|192.168.1.1|11",
                    DestinationPrefix = "203.0.113.1/24",
                    InterfaceIndex = 11
                },
                AddPrefixStep with
                {
                    Identity = "203.0.113.2/24|192.168.1.1|12",
                    DestinationPrefix = "203.0.113.2/24",
                    InterfaceIndex = 12
                }
            ]
        };

        RuntimeExecutionResult result =
            await context.Executor.ExecuteAsync(plan);

        Assert.Equal(
            RuntimeExecutionResultStatus.Failed,
            result.Status);
        Assert.All(
            result.StepResults,
            sr => Assert.Equal(
                RuntimeExecutionStepStatus.Failed,
                sr.Status));
        Assert.Equal(0, context.Api.AddCallCount);
        Assert.Equal(0, context.Api.DeleteCallCount);
        Assert.Empty(
            (await context.RouteInventory.LoadAsync()).Routes);
    }

    [Fact]
    public async Task RouteCreateFault_FirstGroupFailure_SkipsSubsequentPrefixGroup_ProgressDeterministic()
    {
        ExecutorContext context = CreateExecutor(
            FaultInjectionPolicy.For(
                [FaultInjectionPoint.RouteCreate]));
        RuntimeExecutionPlan plan = new()
        {
            Steps = [AddEndpointStep, AddPrefixStep]
        };
        List<RuntimeExecutionProgress> progress = [];

        RuntimeExecutionResult result =
            await context.Executor.ExecuteAsync(
                plan,
                new CollectingProgress(progress));

        Assert.Equal(
            RuntimeExecutionResultStatus.Failed,
            result.Status);
        Assert.Equal(
            RuntimeExecutionStepStatus.Failed,
            result.StepResults[0].Status);
        Assert.Equal(
            RuntimeExecutionStepStatus.Skipped,
            result.StepResults[1].Status);

        RuntimeExecutionProgress last = progress[^1];
        Assert.Equal(2, last.TotalSteps);
        Assert.Equal(2, last.ProcessedSteps);
        Assert.Equal(0, last.SucceededSteps);
        Assert.Equal(1, last.FailedSteps);
        Assert.Equal(0, last.CancelledSteps);
        Assert.Equal(1, last.SkippedSteps);
    }

    [Fact]
    public async Task RouteDeleteFault_EndpointRemove_StepFailed_OwnershipRetained_NoNativeDeletes()
    {
        ExecutorContext context = CreateExecutor(
            FaultInjectionPolicy.For(
                [FaultInjectionPoint.RouteDelete]));
        context.Api.Present.Add(AddEndpointStep.Identity);
        context.EndpointInventory.SeedOwned(
            AddEndpointStep.Identity,
            addedByIranDirect: true);
        RuntimeExecutionPlan plan = new()
        {
            Steps = [RemoveEndpointStep]
        };

        RuntimeExecutionResult result =
            await context.Executor.ExecuteAsync(plan);

        Assert.Equal(
            RuntimeExecutionResultStatus.Failed,
            result.Status);

        RuntimeExecutionStepResult step =
            Assert.Single(result.StepResults);
        Assert.Equal(
            RuntimeExecutionStepStatus.Failed,
            step.Status);
        Assert.Contains(
            "Fault injected at RouteDelete",
            step.ErrorMessage);

        Assert.Equal(0, context.Api.DeleteCallCount);
        Assert.Single(
            (await context.EndpointInventory.LoadAsync()).Endpoints);
    }

    [Fact]
    public async Task RouteDeleteFault_PrefixRemove_StepFailed_OwnershipRetained_GroupStops()
    {
        ExecutorContext context = CreateExecutor(
            FaultInjectionPolicy.For(
                [FaultInjectionPoint.RouteDelete]));
        context.Api.Present.Add(AddPrefixStep.Identity);
        context.RouteInventory.Seed(AddPrefixStep.Identity);
        RuntimeExecutionPlan plan = new()
        {
            Steps = [RemovePrefixStep, RemoveEndpointStep]
        };
        List<RuntimeExecutionProgress> progress = [];

        RuntimeExecutionResult result =
            await context.Executor.ExecuteAsync(
                plan,
                new CollectingProgress(progress));

        Assert.Equal(
            RuntimeExecutionResultStatus.Failed,
            result.Status);
        Assert.Equal(
            RuntimeExecutionStepStatus.Failed,
            result.StepResults[0].Status);
        Assert.Contains(
            "still exists",
            result.StepResults[0].ErrorMessage,
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            RuntimeExecutionStepStatus.Skipped,
            result.StepResults[1].Status);

        Assert.Equal(0, context.Api.DeleteCallCount);
        Assert.Contains(
            AddPrefixStep.Identity,
            (await context.RouteInventory.LoadAsync())
                .Routes.Select(r => r.Identity));

        RuntimeExecutionProgress last = progress[^1];
        Assert.Equal(2, last.TotalSteps);
        Assert.Equal(2, last.ProcessedSteps);
        Assert.Equal(0, last.SucceededSteps);
        Assert.Equal(1, last.FailedSteps);
        Assert.Equal(1, last.SkippedSteps);
    }

    [Fact]
    public async Task RouteEnumerationFault_EndpointAdd_Propagates_NoExecutionBegins()
    {
        ExecutorContext context = CreateExecutor(
            FaultInjectionPolicy.For(
                [FaultInjectionPoint.RouteEnumeration]));
        RuntimeExecutionPlan plan = new()
        {
            Steps = [AddEndpointStep]
        };

        FaultInjectionException exception =
            await Assert.ThrowsAsync<FaultInjectionException>(
                () => context.Executor.ExecuteAsync(plan));

        Assert.Equal(
            FaultInjectionPoint.RouteEnumeration,
            exception.Point);
        Assert.Equal(0, context.Api.AddCallCount);
        Assert.Empty(
            (await context.EndpointInventory.LoadAsync()).Endpoints);
    }

    [Fact]
    public async Task RouteEnumerationFault_PrefixVerification_ReturnsFailedStep()
    {
        ExecutorContext context = CreateExecutor(
            FaultInjectionPolicy.For(
                [FaultInjectionPoint.RouteEnumeration]));
        RuntimeExecutionPlan plan = new()
        {
            Steps = [AddPrefixStep]
        };

        RuntimeExecutionResult result =
            await context.Executor.ExecuteAsync(plan);

        Assert.Equal(
            RuntimeExecutionResultStatus.Failed,
            result.Status);

        RuntimeExecutionStepResult step =
            Assert.Single(result.StepResults);
        Assert.Equal(
            RuntimeExecutionStepStatus.Failed,
            step.Status);
    }

    [Fact]
    public async Task RouteCreateFault_NextUnfaultedOperation_Recovers()
    {
        ExecutorContext context = CreateExecutor(
            faultPolicy: null);
        RuntimeExecutionPlan plan = new()
        {
            Steps = [AddEndpointStep]
        };

        RuntimeExecutionResult first;
        using (FaultInjectionScope scope =
            FaultInjectionScope.Fail(
                FaultInjectionPoint.RouteCreate))
        {
            first = await context.Executor.ExecuteAsync(plan);
        }

        Assert.Equal(
            RuntimeExecutionResultStatus.Failed,
            first.Status);
        Assert.Equal(0, context.Api.AddCallCount);

        RuntimeExecutionResult second =
            await context.Executor.ExecuteAsync(plan);

        Assert.Equal(
            RuntimeExecutionResultStatus.Completed,
            second.Status);
        Assert.True(second.MutatedInfrastructure);
        Assert.Equal(1, context.Api.AddCallCount);
        Assert.Single(
            (await context.EndpointInventory.LoadAsync()).Endpoints);
    }

    private static ExecutorContext CreateExecutor(
        IFaultInjectionPolicy? faultPolicy)
    {
        FakeWindowsRouteApi api = new();
        WindowsRouteManager routeManager =
            new(new CommandRunner(), api, faultPolicy);
        FakeRouteInventory routeInventory = new();
        FakeEndpointInventory endpointInventory = new();
        WindowsRuntimeExecutionStepHandler handler =
            new(routeManager, routeInventory, endpointInventory);
        RuntimeExecutor executor = new(handler);

        return new ExecutorContext(
            executor,
            api,
            routeInventory,
            endpointInventory);
    }

    private sealed record ExecutorContext(
        RuntimeExecutor Executor,
        FakeWindowsRouteApi Api,
        FakeRouteInventory RouteInventory,
        FakeEndpointInventory EndpointInventory);

    private sealed class CollectingProgress
        : IProgress<RuntimeExecutionProgress>
    {
        private readonly List<RuntimeExecutionProgress> _reports;

        public CollectingProgress(List<RuntimeExecutionProgress> reports)
        {
            _reports = reports;
        }

        public void Report(RuntimeExecutionProgress value)
        {
            _reports.Add(value);
        }
    }

    private sealed class FakeWindowsRouteApi : IWindowsRouteApi
    {
        public HashSet<string> Present { get; } =
            new(StringComparer.OrdinalIgnoreCase);

        public int EnumerateCallCount { get; private set; }

        public int AddCallCount { get; private set; }

        public int DeleteCallCount { get; private set; }

        public Task<IReadOnlyList<SystemRoute>> EnumerateAsync(
            CancellationToken cancellationToken = default)
        {
            EnumerateCallCount++;
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult<IReadOnlyList<SystemRoute>>(
                Present
                    .Select(ToSystemRoute)
                    .ToArray());
        }

        public Task AddAsync(
            IReadOnlyCollection<ManagedRoute> routes,
            CancellationToken cancellationToken = default)
        {
            AddCallCount++;
            cancellationToken.ThrowIfCancellationRequested();

            foreach (ManagedRoute route in routes)
            {
                Present.Add(route.Identity);
            }

            return Task.CompletedTask;
        }

        public Task DeleteAsync(
            IReadOnlyCollection<ManagedRoute> routes,
            CancellationToken cancellationToken = default)
        {
            DeleteCallCount++;
            cancellationToken.ThrowIfCancellationRequested();

            foreach (ManagedRoute route in routes)
            {
                Present.Remove(route.Identity);
            }

            return Task.CompletedTask;
        }

        private static SystemRoute ToSystemRoute(string identity)
        {
            string[] parts = identity.Split('|');

            return new SystemRoute
            {
                DestinationPrefix = parts[0],
                NextHop = IPAddress.Parse(parts[1]),
                InterfaceIndex = uint.Parse(parts[2]),
                RouteMetric = 256
            };
        }
    }

    private sealed class FakeRouteInventory : IRouteInventoryPersistence
    {
        private RouteInventory? _stored = new();

        public void Seed(string identity)
        {
            string[] parts = identity.Split('|');

            _stored = new RouteInventory
            {
                Routes =
                [
                    new RouteInventoryItem
                    {
                        DestinationPrefix = parts[0],
                        Gateway = parts[1],
                        InterfaceIndex = uint.Parse(parts[2]),
                        Metric = 256
                    }
                ]
            };
        }

        public Task<RouteInventory> LoadAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_stored ?? new RouteInventory());

        public Task SaveAsync(
            RouteInventory inventory,
            CancellationToken cancellationToken = default)
        {
            _stored = inventory;
            return Task.CompletedTask;
        }

        public Task MutateAsync(
            Func<RouteInventory, RouteInventory> transform,
            CancellationToken cancellationToken = default)
        {
            RouteInventory current = _stored ?? new RouteInventory();
            _stored = transform(current);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeEndpointInventory
        : IEndpointInventoryPersistence
    {
        private VpnEndpointInventory? _stored = new();

        public void SeedOwned(string identity, bool addedByIranDirect)
        {
            string[] parts = identity.Split('|');

            _stored = new VpnEndpointInventory
            {
                Endpoints =
                [
                    new VpnEndpointInventoryItem
                    {
                        Host = "test.example.com",
                        Address = parts[1],
                        Port = 1194,
                        Protocol = "udp",
                        DestinationPrefix = parts[0],
                        Gateway = parts[1],
                        InterfaceIndex = uint.Parse(parts[2]),
                        Metric = 1,
                        AddedByIranDirect = addedByIranDirect,
                        IsCurrent = true,
                        ProtectedAt = DateTimeOffset.UtcNow,
                        LastSeenAt = DateTimeOffset.UtcNow
                    }
                ]
            };
        }

        public Task<VpnEndpointInventory> LoadAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_stored ?? new VpnEndpointInventory());

        public Task SaveAsync(
            VpnEndpointInventory inventory,
            CancellationToken cancellationToken = default)
        {
            _stored = inventory;
            return Task.CompletedTask;
        }

        public Task MutateAsync(
            Func<VpnEndpointInventory, VpnEndpointInventory> transform,
            CancellationToken cancellationToken = default)
        {
            VpnEndpointInventory current =
                _stored ?? new VpnEndpointInventory();
            _stored = transform(current);
            return Task.CompletedTask;
        }
    }
}
