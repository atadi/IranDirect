namespace IranDirect.Core.Tests;

using System.Net;
using IranDirect.Core.Configuration;
using IranDirect.Core.Networking;
using IranDirect.Core.Prefixes;
using IranDirect.Core.Routing;
using IranDirect.Core.Runtime;
using IranDirect.Core.Runtime.Execution;
using IranDirect.Core.Runtime.Reconciliation;
using IranDirect.Core.State;
using IranDirect.Core.Vpn;

public sealed class IranDirectControllerTests
{
    [Fact]
    public async Task Enable_CompletedExecution_UpdatesStateEnabled()
    {
        await using TestContext ctx = new();
        ctx.ExecutorResult = RuntimeExecutionResult.Completed([
            new RuntimeExecutionStepResult
            {
                StepIdentity = "test|id",
                Status = RuntimeExecutionStepStatus.Succeeded
            }
        ]);
        ctx.DesiredEnabled = true;

        RuntimeCycleExecutionResult result = await ctx.Controller.EnableAsync();

        Assert.True(result.IsSuccess);
        IranDirectState state = await ctx.StateRepository.LoadAsync();
        Assert.True(state.Enabled);
        Assert.Null(state.LastError);
    }

    [Fact]
    public async Task Enable_NoExecutionRequired_UpdatesStateEnabled()
    {
        await using TestContext ctx = new();
        ctx.ExecutorResult = RuntimeExecutionResult.NoExecutionRequired();
        ctx.DesiredEnabled = true;

        RuntimeCycleExecutionResult result = await ctx.Controller.EnableAsync();

        Assert.True(result.IsSuccess);
        IranDirectState state = await ctx.StateRepository.LoadAsync();
        Assert.True(state.Enabled);
    }

    [Fact]
    public async Task Enable_FailedExecution_DoesNotUpdateState()
    {
        await using TestContext ctx = new();
        ctx.ExecutorResult = RuntimeExecutionResult.Failed([
            new RuntimeExecutionStepResult
            {
                StepIdentity = "test|id",
                Status = RuntimeExecutionStepStatus.Failed,
                ErrorMessage = "test failure"
            }
        ], "test failure");

        RuntimeCycleExecutionResult result = await ctx.Controller.EnableAsync();

        Assert.False(result.IsSuccess);
        IranDirectState state = await ctx.StateRepository.LoadAsync();
        Assert.False(state.Enabled);
        Assert.Equal("test failure", state.LastError);
    }

    [Fact]
    public async Task Enable_CancelledExecution_DoesNotUpdateState()
    {
        await using TestContext ctx = new();
        ctx.ExecutorResult = RuntimeExecutionResult.Cancelled([
            new RuntimeExecutionStepResult
            {
                StepIdentity = "test|id",
                Status = RuntimeExecutionStepStatus.Cancelled
            }
        ], "cancelled");

        RuntimeCycleExecutionResult result = await ctx.Controller.EnableAsync();

        Assert.False(result.IsSuccess);
        IranDirectState state = await ctx.StateRepository.LoadAsync();
        Assert.False(state.Enabled);
    }

    [Fact]
    public async Task Enable_PartiallyCompleted_DoesNotUpdateState()
    {
        await using TestContext ctx = new();
        ctx.ExecutorResult = RuntimeExecutionResult.PartiallyCompleted([
            new RuntimeExecutionStepResult
            {
                StepIdentity = "first|id",
                Status = RuntimeExecutionStepStatus.Succeeded
            },
            new RuntimeExecutionStepResult
            {
                StepIdentity = "second|id",
                Status = RuntimeExecutionStepStatus.Failed,
                ErrorMessage = "partial failure"
            }
        ], "partial failure");

        RuntimeCycleExecutionResult result = await ctx.Controller.EnableAsync();

        Assert.False(result.IsSuccess);
        IranDirectState state = await ctx.StateRepository.LoadAsync();
        Assert.False(state.Enabled);
    }

    [Fact]
    public async Task Disable_CompletedExecution_UpdatesStateDisabled()
    {
        await using TestContext ctx = new();
        ctx.ExecutorResult = RuntimeExecutionResult.Completed([
            new RuntimeExecutionStepResult
            {
                StepIdentity = "test|id",
                Status = RuntimeExecutionStepStatus.Succeeded
            }
        ]);

        RuntimeCycleExecutionResult result = await ctx.Controller.DisableAsync();

        Assert.True(result.IsSuccess);
        IranDirectState state = await ctx.StateRepository.LoadAsync();
        Assert.False(state.Enabled);
        Assert.Null(state.LastError);
    }

    [Fact]
    public async Task Disable_NoExecutionRequired_UpdatesStateDisabled()
    {
        await using TestContext ctx = new();
        ctx.ExecutorResult = RuntimeExecutionResult.NoExecutionRequired();

        RuntimeCycleExecutionResult result = await ctx.Controller.DisableAsync();

        Assert.True(result.IsSuccess);
        IranDirectState state = await ctx.StateRepository.LoadAsync();
        Assert.False(state.Enabled);
    }

    [Fact]
    public async Task Disable_FailedExecution_DoesNotChangeState()
    {
        await using TestContext ctx = new();
        await ctx.StateRepository.SaveAsync(new IranDirectState
        {
            Enabled = true,
            Gateway = "192.168.1.1",
            InterfaceIndex = 10,
            InterfaceName = "Ethernet",
            PrefixCount = 1,
            EnabledAt = DateTimeOffset.UtcNow
        });
        ctx.ExecutorResult = RuntimeExecutionResult.Failed([
            new RuntimeExecutionStepResult
            {
                StepIdentity = "test|id",
                Status = RuntimeExecutionStepStatus.Failed,
                ErrorMessage = "test failure"
            }
        ], "test failure");

        RuntimeCycleExecutionResult result = await ctx.Controller.DisableAsync();

        Assert.False(result.IsSuccess);
        IranDirectState state = await ctx.StateRepository.LoadAsync();
        Assert.True(state.Enabled);
    }

    [Fact]
    public async Task Enable_DecisionAndExecutionPreservedInResult()
    {
        await using TestContext ctx = new();
        ctx.ExecutorResult = RuntimeExecutionResult.NoExecutionRequired();
        ctx.DesiredEnabled = true;

        RuntimeCycleExecutionResult result = await ctx.Controller.EnableAsync();

        Assert.NotNull(result.Decision);
        Assert.NotNull(result.Execution);
        Assert.Same(ctx.FakeExecutor.Result, result.Execution);
    }

    [Fact]
    public async Task Disable_DecisionAndExecutionPreservedInResult()
    {
        await using TestContext ctx = new();
        ctx.ExecutorResult = RuntimeExecutionResult.NoExecutionRequired();

        RuntimeCycleExecutionResult result = await ctx.Controller.DisableAsync();

        Assert.NotNull(result.Decision);
        Assert.NotNull(result.Execution);
        Assert.Same(ctx.FakeExecutor.Result, result.Execution);
    }

    [Fact]
    public async Task Enable_DesiredDisabled_DoesNotEnableState()
    {
        await using TestContext ctx = new();
        ctx.ExecutorResult = RuntimeExecutionResult.NoExecutionRequired();
        ctx.DesiredEnabled = false;

        RuntimeCycleExecutionResult result = await ctx.Controller.EnableAsync();

        Assert.True(result.IsSuccess);
        IranDirectState state = await ctx.StateRepository.LoadAsync();
        Assert.False(state.Enabled);
    }

    [Fact]
    public async Task Enable_ExecutesPlanFromDecision()
    {
        await using TestContext ctx = new();
        RuntimeExecutionStep expectedStep = new()
        {
            Kind = RuntimeExecutionStepKind.AddPrefixRoute,
            Identity = "203.0.113.0/24|192.168.1.1|10",
            DestinationPrefix = "203.0.113.0/24",
            Gateway = "192.168.1.1",
            InterfaceIndex = 10,
            Metric = 5,
            Description = "Test step"
        };
        RuntimeExecutionPlan plan = new() { Steps = [expectedStep] };
        ctx.DesiredEnabled = true;
        ctx.DecisionPlan = plan;
        ctx.ExecutorResult = RuntimeExecutionResult.Completed([
            new RuntimeExecutionStepResult
            {
                StepIdentity = expectedStep.Identity,
                Status = RuntimeExecutionStepStatus.Succeeded
            }
        ]);

        await ctx.Controller.EnableAsync();

        RuntimeExecutionPlan? executedPlan = ctx.FakeExecutor.ExecutedPlan;
        Assert.NotNull(executedPlan);
        Assert.Same(plan, executedPlan);
    }

    [Fact]
    public async Task Disable_UsesSamePipeline()
    {
        await using TestContext ctx = new();
        ctx.ExecutorResult = RuntimeExecutionResult.NoExecutionRequired();

        await ctx.Controller.DisableAsync();

        RuntimeExecutionPlan? executedPlan = ctx.FakeExecutor.ExecutedPlan;
        Assert.NotNull(executedPlan);
    }

    [Fact]
    public async Task Disable_MutatedInfrastructureTrue_ClearsRouteInventory()
    {
        await using TestContext ctx = new();

        RouteInventory initialInventory = new()
        {
            Routes =
            [
                new RouteInventoryItem
                {
                    DestinationPrefix = "203.0.113.0/24",
                    Gateway = "192.168.1.1",
                    InterfaceIndex = 10,
                    Metric = 5
                }
            ]
        };
        await ctx.RouteInventoryStore.SaveAsync(initialInventory);

        ctx.ExecutorResult = RuntimeExecutionResult.Completed([
            new RuntimeExecutionStepResult
            {
                StepIdentity = "test|id",
                Status = RuntimeExecutionStepStatus.Succeeded
            }
        ]);
        ctx.DesiredEnabled = false;

        await ctx.Controller.DisableAsync();

        RouteInventory inventory = await ctx.RouteInventoryStore.LoadAsync();
        Assert.Empty(inventory.Routes);
    }

    [Fact]
    public async Task Enable_CancellationTokenReachesCoordinatorAndExecutor()
    {
        await using TestContext ctx = new();
        using CancellationTokenSource cts = new();
        CancellationToken token = cts.Token;
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => ctx.Controller.EnableAsync(token));
    }

    internal sealed class FakeDecisionBuilder : IRuntimeDecisionBuilder
    {
        public RuntimeDecision? Decision { get; set; }

        public Task<RuntimeDecision> BuildAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Decision!);
        }
    }

    internal sealed class FakeExecutor : IRuntimeExecutor
    {
        public RuntimeExecutionResult Result { get; set; } =
            RuntimeExecutionResult.NoExecutionRequired();

        public RuntimeExecutionPlan? ExecutedPlan { get; private set; }

        public Task<RuntimeExecutionResult> ExecuteAsync(
            RuntimeExecutionPlan plan,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ExecutedPlan = plan;
            return Task.FromResult(Result);
        }
    }

    internal sealed class FakeRouteManager : IRouteManager
    {
        public HashSet<string> Present { get; set; } = [];

        public Task AddRoutesAsync(
            IReadOnlyCollection<ManagedRoute> routes,
            CancellationToken cancellationToken = default)
        {
            foreach (ManagedRoute route in routes)
                Present.Add(route.Identity);
            return Task.CompletedTask;
        }

        public Task DeleteRoutesAsync(
            IReadOnlyCollection<ManagedRoute> routes,
            CancellationToken cancellationToken = default)
        {
            foreach (ManagedRoute route in routes)
                Present.Remove(route.Identity);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<SystemRoute>> GetIpv4RoutesAsync(
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<SystemRoute> routes = Present
                .Select(id =>
                {
                    string[] parts = id.Split('|', 3);
                    return new SystemRoute
                    {
                        DestinationPrefix = parts[0],
                        NextHop = IPAddress.Parse(parts[1]),
                        InterfaceIndex = uint.Parse(parts[2])
                    };
                })
                .ToList();
            return Task.FromResult(routes);
        }
    }

    private sealed class TestContext : IAsyncDisposable
    {
        private readonly string _tempDir;

        public StateRepository StateRepository { get; }
        public RouteInventoryStore RouteInventoryStore { get; }
        public IranDirectController Controller { get; }
        public FakeExecutor FakeExecutor { get; }
        public FakeDecisionBuilder FakeDecisionBuilder { get; }

        public RuntimeExecutionResult ExecutorResult
        {
            get => FakeExecutor.Result;
            set => FakeExecutor.Result = value;
        }

        public bool DesiredEnabled
        {
            get => false;
            set
            {
                RuntimePlanSnapshot plan = CreatePlan(value);
                RuntimeReconciliationResult reconciliation =
                    RuntimeReconciliationResult.NoChanges();
                RuntimeExecutionPlan executionPlan = new() { Steps = [] };
                RuntimeDecision decision = RuntimeDecision.Create(
                    plan, reconciliation, executionPlan, DateTimeOffset.UtcNow);
                FakeDecisionBuilder.Decision = decision;
            }
        }

        public RuntimeExecutionPlan DecisionPlan
        {
            set
            {
                RuntimePlanSnapshot plan = CreatePlan(true);
                RuntimeChange[] changes = value.Steps
                    .Select(step => new RuntimeChange
                    {
                        Kind = MapKind(step.Kind),
                        Identity = step.Identity,
                        DestinationPrefix = step.DestinationPrefix,
                        Gateway = step.Gateway,
                        InterfaceIndex = step.InterfaceIndex,
                        Metric = step.Metric,
                        Description = step.Description
                    })
                    .ToArray();
                RuntimeReconciliationResult reconciliation =
                    RuntimeReconciliationResult.Planned(
                        new RuntimeChangeSet { Changes = changes });
                RuntimeDecision decision = RuntimeDecision.Create(
                    plan, reconciliation, value, DateTimeOffset.UtcNow);
                FakeDecisionBuilder.Decision = decision;
            }
        }

        public TestContext()
        {
            _tempDir = Path.Combine(
                Path.GetTempPath(), $"IranDirectTest_{Guid.NewGuid()}");
            Directory.CreateDirectory(_tempDir);

            StateRepository = new StateRepository(
                Path.Combine(_tempDir, "state.json"));
            RouteInventoryStore = new RouteInventoryStore(
                Path.Combine(_tempDir, "route-inventory.json"));
            VpnEndpointInventoryStore endpointInventory = new(
                Path.Combine(_tempDir, "endpoint-inventory.json"));

            PrefixFileRepository prefixRepo = new(
                Path.Combine(_tempDir, "prefixes.txt"));
            File.WriteAllText(
                Path.Combine(_tempDir, "prefixes.txt"),
                "203.0.113.0/24" + Environment.NewLine);

            DesiredConfigurationStore configStore = new(
                Path.Combine(_tempDir, "config.json"),
                new DesiredConfigurationValidator());
            DesiredConfigurationService configService = new(configStore);

            FakeExecutor = new FakeExecutor();
            FakeDecisionBuilder = new FakeDecisionBuilder();
            RuntimeCycleCoordinator coordinator = new(FakeDecisionBuilder);

            FakeRouteManager routeManager = new();
            GatewayDetector gatewayDetector = new();
            OpenVpnEndpointProvider vpnProvider = new(
                Path.Combine(_tempDir, "vpn-profile.ovpn"),
                new OpenVpnProfileParser(),
                new VpnEndpointResolver());
            VpnEndpointRouteManager vpnRouteManager = new(routeManager);

            Controller = new IranDirectController(
                null!, // IranPrefixProvider - not called
                prefixRepo,
                gatewayDetector,
                routeManager,
                StateRepository,
                RouteInventoryStore,
                vpnProvider,
                vpnRouteManager,
                endpointInventory,
                coordinator,
                FakeExecutor,
                configService);

            // Set a default decision so first call doesn't NPE
            DesiredEnabled = false;
        }

        public async ValueTask DisposeAsync()
        {
            try { Directory.Delete(_tempDir, recursive: true); }
            catch { /* ok */ }
            await ValueTask.CompletedTask;
        }

        private static RuntimePlanSnapshot CreatePlan(bool enabled)
        {
            return new RuntimePlanSnapshot
            {
                Configuration = new DesiredConfiguration { Enabled = enabled },
                Observed = new ObservedRuntime
                {
                    VpnProfileExists = true,
                    VpnProfileValid = true,
                    DirectGateway = new ObservedDirectGateway
                    {
                        Address = "192.168.1.1",
                        InterfaceIndex = 10,
                        InterfaceName = "Ethernet"
                    },
                    VpnEndpoints =
                    [
                        new ObservedVpnEndpoint
                        {
                            Host = "vpn.example.com",
                            Address = "10.0.0.1",
                            Port = 1194,
                            Protocol = "udp"
                        }
                    ],
                    Prefixes = ["203.0.113.0/24"],
                    Routes = [],
                    ObservedAt = DateTimeOffset.UtcNow
                },
                Desired = new DesiredRuntime
                {
                    Enabled = enabled,
                    Blockers = [],
                    EndpointRoutes =
                    [
                        new DesiredEndpointRoute
                        {
                            Host = "vpn.example.com",
                            Address = "10.0.0.1",
                            Port = 1194,
                            Protocol = "udp",
                            DestinationPrefix = "10.0.0.1/32",
                            Gateway = "192.168.1.1",
                            InterfaceIndex = 10,
                            Metric = 1
                        }
                    ],
                    PrefixRoutes = enabled
                        ? [new DesiredPrefixRoute
                        {
                            DestinationPrefix = "203.0.113.0/24",
                            Gateway = "192.168.1.1",
                            InterfaceIndex = 10,
                            Metric = 5
                        }]
                        : []
                },
                PlannedAt = DateTimeOffset.UtcNow
            };
        }

        private static RuntimeChangeKind MapKind(
            RuntimeExecutionStepKind kind) => kind switch
        {
            RuntimeExecutionStepKind.AddEndpointRoute =>
                RuntimeChangeKind.AddEndpointRoute,
            RuntimeExecutionStepKind.RemoveEndpointRoute =>
                RuntimeChangeKind.RemoveEndpointRoute,
            RuntimeExecutionStepKind.AddPrefixRoute =>
                RuntimeChangeKind.AddPrefixRoute,
            RuntimeExecutionStepKind.RemovePrefixRoute =>
                RuntimeChangeKind.RemovePrefixRoute,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }
}
