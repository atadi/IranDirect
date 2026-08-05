using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using IranDirect.Core;
using IranDirect.Core.Configuration;
using IranDirect.Core.Networking;
using IranDirect.Core.Observability.Telemetry;
using IranDirect.Core.Prefixes;
using IranDirect.Core.Routing;
using IranDirect.Core.Runtime;
using IranDirect.Core.Runtime.Execution;
using IranDirect.Core.Runtime.Profiling;
using IranDirect.Core.Runtime.Reconciliation;
using IranDirect.Core.State;
using IranDirect.Core.Testing.FaultInjection;
using IranDirect.Core.Vpn;
using Xunit;

namespace IranDirect.Core.Tests.Observability.Telemetry;

/// <summary>
/// Runtime-execution telemetry instrumentation tests. Reuses the production
/// <see cref="IranDirectController"/> with lightweight fakes and captures the
/// <c>Runtime.Execute</c> child activity + metrics via BCL <see cref="ActivityListener"/>
/// and <see cref="MeterListener"/> only. No OpenTelemetry packages.
///
/// Static counters/histograms persist across tests, so every test asserts
/// per-listener deltas captured during its own listener lifetime, never
/// process-global totals.
/// </summary>
[Collection("RuntimeCycleTelemetry")]
public sealed class RuntimeExecutionTelemetryTests
{
    private sealed class Harness : IAsyncDisposable
    {
        private readonly string _tempDir;

        public StateRepository StateRepository { get; }
        public RouteInventoryStore RouteInventoryStore { get; }
        public IranDirectController Controller { get; }
        public IranDirectControllerTests.FakeExecutor FakeExecutor { get; }
        public IranDirectControllerTests.FakeDecisionBuilder FakeDecisionBuilder { get; }
        public RuntimeOperationStatus OperationStatus { get; }
        public RuntimeCycleProfiler Profiler { get; }

        public Harness(string? perfDirectory = null, bool useRealReconciler = false)
        {
            _tempDir = Path.Combine(
                Path.GetTempPath(), $"IranDirectExecTele_{Guid.NewGuid()}");
            Directory.CreateDirectory(_tempDir);
            File.WriteAllText(
                Path.Combine(_tempDir, "p.txt"),
                "203.0.113.0/24" + Environment.NewLine);

            Profiler = new RuntimeCycleProfiler(
                enabled: true,
                store: perfDirectory is null
                    ? null
                    : new RuntimePerfReportStore(perfDirectory));
            StateRepository = new StateRepository(
                Path.Combine(_tempDir, "state.json"));
            RouteInventoryStore = new RouteInventoryStore(
                Path.Combine(_tempDir, "route-inventory.json"));

            DesiredConfigurationStore configStore = new(
                Path.Combine(_tempDir, "config.json"),
                new DesiredConfigurationValidator());
            DesiredConfigurationService configService = new(configStore);

            FakeExecutor = new IranDirectControllerTests.FakeExecutor();
            FakeDecisionBuilder = new IranDirectControllerTests.FakeDecisionBuilder();

            RuntimeCycleCoordinator coordinator = useRealReconciler
                ? BuildRealReconcilerCoordinator()
                : new RuntimeCycleCoordinator(FakeDecisionBuilder);
            OperationStatus = new RuntimeOperationStatus();
            IranDirectControllerTests.FakeRouteManager routeManager =
                new IranDirectControllerTests.FakeRouteManager();
            GatewayDetector gatewayDetector = new();
            OpenVpnEndpointProvider vpnProvider = new(
                Path.Combine(_tempDir, "vpn.ovpn"),
                new OpenVpnProfileParser(),
                new VpnEndpointResolver());
            VpnEndpointRouteManager vpnRouteManager = new(routeManager);

            SetDesiredEnabled(configService, true);

            Controller = new IranDirectController(
                null!,
                new PrefixFileRepository(Path.Combine(_tempDir, "p.txt")),
                gatewayDetector,
                routeManager,
                StateRepository,
                RouteInventoryStore,
                vpnProvider,
                vpnRouteManager,
                new VpnEndpointInventoryStore(
                    Path.Combine(_tempDir, "ep.json")),
                coordinator,
                FakeExecutor,
                configService,
                OperationStatus,
                Profiler);
        }

        public RuntimeExecutionResult ExecutorResult
        {
            get => FakeExecutor.Result;
            set => FakeExecutor.Result = value;
        }

        public void SetDecisionPlan(int stepCount)
        {
            RuntimePlanSnapshot plan = CreatePlan(true);
            var steps = new List<RuntimeExecutionStep>();
            var changes = new List<RuntimeChange>();

            for (int i = 0; i < stepCount; i++)
            {
                string identity = $"test|step|{i}";
                RuntimeExecutionStepKind kind = RuntimeExecutionStepKind.AddPrefixRoute;
                steps.Add(new RuntimeExecutionStep
                {
                    Kind = kind,
                    Identity = identity,
                    DestinationPrefix = "test",
                    Gateway = "192.168.1.1",
                    InterfaceIndex = 10,
                    Metric = 5,
                    Description = $"step {i}",
                });
                changes.Add(new RuntimeChange
                {
                    Kind = RuntimeChangeKind.AddPrefixRoute,
                    Identity = identity,
                    DestinationPrefix = "test",
                    Gateway = "192.168.1.1",
                    InterfaceIndex = 10,
                    Metric = 5,
                    Description = $"change {i}",
                });
            }

            RuntimeReconciliationResult reconciliation =
                RuntimeReconciliationResult.Planned(
                    new RuntimeChangeSet { Changes = changes });
            RuntimeExecutionPlan executionPlan = new() { Steps = steps };
            RuntimeDecision decision = RuntimeDecision.Create(
                plan, reconciliation, executionPlan, DateTimeOffset.UtcNow);
            FakeDecisionBuilder.Decision = decision;
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
                        InterfaceName = "Ethernet",
                    },
                    VpnEndpoints =
                    [
                        new ObservedVpnEndpoint
                        {
                            Host = "vpn.example.com",
                            Address = "10.0.0.1",
                            Port = 1194,
                            Protocol = "udp",
                        },
                    ],
                    Prefixes = ["203.0.113.0/24"],
                    Routes = [],
                    ObservedAt = DateTimeOffset.UtcNow,
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
                            Metric = 1,
                        },
                    ],
                    PrefixRoutes = enabled
                        ? [new DesiredPrefixRoute
                        {
                            DestinationPrefix = "203.0.113.0/24",
                            Gateway = "192.168.1.1",
                            InterfaceIndex = 10,
                            Metric = 5,
                        }]
                        : [],
                },
                PlannedAt = DateTimeOffset.UtcNow,
            };
        }

        private void SetDesiredEnabled(
            DesiredConfigurationService svc,
            bool enabled)
        {
            RuntimePlanSnapshot plan = CreatePlan(enabled);
            RuntimeReconciliationResult reconciliation =
                RuntimeReconciliationResult.NoChanges();
            RuntimeExecutionPlan executionPlan = new() { Steps = [] };
            RuntimeDecision decision = RuntimeDecision.Create(
                plan, reconciliation, executionPlan, DateTimeOffset.UtcNow);
            FakeDecisionBuilder.Decision = decision;
            svc.SetEnabledAsync(enabled, CancellationToken.None)
                .GetAwaiter().GetResult();
        }

        // Builds a RuntimeCycleCoordinator backed by the REAL
        // RuntimeDecisionBuilder -> RuntimeReconciler, so that a controller
        // RunCycleAsync emits the real Runtime.PlanChanges activity (under the
        // cycle root started by the controller) and then the controller's
        // Runtime.Execute wrap. This is what lets the sibling-hierarchy test
        // observe both child activities using the production flow.
        private static RuntimeCycleCoordinator BuildRealReconcilerCoordinator()
        {
            IRuntimePlanCoordinator planCoordinator =
                new StubPlanCoordinator(CreatePlan(true));
            var reconciler = new RuntimeReconciler(
                new RuntimeRouteOwnershipProvider(new StubOwnershipSource()),
                new RuntimeChangeSetPlanner());
            var decisionBuilder = new RuntimeDecisionBuilder(
                planCoordinator,
                reconciler,
                new RuntimeExecutionPlanner(),
                TimeProvider.System);
            return new RuntimeCycleCoordinator(decisionBuilder);
        }

        public async ValueTask DisposeAsync()
        {
            try { Directory.Delete(_tempDir, recursive: true); }
            catch { /* best-effort */ }
            await Task.CompletedTask;
        }
    }

    private static ActivityListener CreateActivityListener(
        ConcurrentQueue<Activity> stopped,
        ConcurrentQueue<Activity> started)
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == IranDirectTelemetry.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = a => started.Enqueue(a),
            ActivityStopped = a => stopped.Enqueue(a),
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    private sealed record MetricCapture(
        long DurationCount, double DurationSum,
        long OperationsCount, double OperationsSum)
    {
        public static MetricCapture From(
            ConcurrentQueue<double> durations,
            ConcurrentQueue<double> operations)
        {
            double dSum = 0, oSum = 0;
            foreach (double d in durations) dSum += d;
            foreach (double o in operations) oSum += o;
            return new MetricCapture(durations.Count, dSum, operations.Count, oSum);
        }
    }

    private static MeterListener CreateMeterListener(
        ConcurrentQueue<double> durations,
        ConcurrentQueue<double> operations)
    {
        var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == IranDirectTelemetry.SourceName)
                meterListener.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<double>(
            (instrument, value, tags, _) =>
            {
                if (instrument.Name == IranDirectMetricNames.RuntimeExecutionDuration)
                    durations.Enqueue(value);
                else if (instrument.Name == IranDirectMetricNames.RuntimeOperationsPerCycle)
                    operations.Enqueue(value);
            });
        listener.Start();
        return listener;
    }

    [Fact]
    public async Task RunCycle_CompletedExecution_EmitsExecuteChildActivity()
    {
        await using var h = new Harness();
        var stopped = new ConcurrentQueue<Activity>();
        var started = new ConcurrentQueue<Activity>();

        using var al = CreateActivityListener(stopped, started);

        h.ExecutorResult = RuntimeExecutionResult.Completed(
        [
            new RuntimeExecutionStepResult
            {
                StepIdentity = "test|id",
                Kind = RuntimeExecutionStepKind.AddPrefixRoute,
                DestinationPrefix = "test",
                Status = RuntimeExecutionStepStatus.Succeeded,
            },
        ]);

        RuntimeCycleExecutionResult result = await h.Controller.RunCycleAsync();

        Assert.True(result.IsSuccess);

        Activity? planning = stopped.SingleOrDefault(a =>
            a.OperationName == IranDirectActivityNames.RuntimePlanChanges);
        Activity? execution = stopped.SingleOrDefault(a =>
            a.OperationName == IranDirectActivityNames.RuntimeExecute);
        Activity? cycle = stopped.SingleOrDefault(a =>
            a.OperationName == IranDirectActivityNames.RuntimeCycle);

        // Exactly one Runtime.Execute child activity, kind Internal.
        Assert.NotNull(execution);
        Assert.Equal(ActivityKind.Internal, execution!.Kind);
        Assert.Equal(
            IranDirectTagValues.OperationExecute,
            execution.Tags.Single(t => t.Key == IranDirectTagNames.Operation).Value);
        Assert.Equal(
            IranDirectTagValues.Success,
            execution.Tags.Single(t => t.Key == IranDirectTagNames.Outcome).Value);
        Assert.Equal(ActivityStatusCode.Ok, execution.Status);

        // Parent/child correlation (controller path with a faked decision
        // builder): Runtime.Execute is a child of the runtime-cycle span and
        // shares its TraceId. In this harness the decision is supplied directly
        // by FakeDecisionBuilder, so Runtime.PlanChanges is not emitted here;
        // the sibling relationship with planning is proven end-to-end by
        // SiblingOfPlanning_UnderRuntimeCycle below using the real reconciler.
        Assert.NotNull(cycle);
        Assert.NotNull(execution);
        Assert.Equal(cycle!.TraceId, execution!.TraceId);
        Assert.Equal(cycle.SpanId, execution.ParentSpanId);

        // Event order: cycle -> execute -> (execute stops) -> cycle stops.
        Activity[] startedByTrace = [.. started.Where(a => a.TraceId == cycle.TraceId)];
        Activity[] stoppedByTrace = [.. stopped.Where(a => a.TraceId == cycle.TraceId)];
        Assert.Equal(2, startedByTrace.Length);
        Assert.Equal(2, stoppedByTrace.Length);
        Assert.Equal(
            IranDirectActivityNames.RuntimeCycle, startedByTrace[0].OperationName);
        Assert.Equal(
            IranDirectActivityNames.RuntimeExecute, startedByTrace[1].OperationName);
        Assert.Equal(
            IranDirectActivityNames.RuntimeExecute, stoppedByTrace[0].OperationName);
        Assert.Equal(
            IranDirectActivityNames.RuntimeCycle, stoppedByTrace[1].OperationName);

        // No extra child spans beyond the approved set.
        Assert.All(stopped, a =>
        {
            Assert.Contains(a.OperationName, new[]
            {
                IranDirectActivityNames.RuntimeCycle,
                IranDirectActivityNames.RuntimePlanChanges,
                IranDirectActivityNames.RuntimeExecute,
            });
        });
    }

    [Fact]
    public async Task RunCycle_CompletedExecution_DurationAndOperationsRecordedOnce()
    {
        await using var h = new Harness();
        var durations = new ConcurrentQueue<double>();
        var operations = new ConcurrentQueue<double>();
        var stopped = new ConcurrentQueue<Activity>();

        using var al = CreateActivityListener(stopped, new ConcurrentQueue<Activity>());
        using var ml = CreateMeterListener(durations, operations);

        int stepCount = 3;
        h.SetDecisionPlan(stepCount);
        h.ExecutorResult = RuntimeExecutionResult.Completed(
        [
            new RuntimeExecutionStepResult
            {
                StepIdentity = $"test|a|{0}",
                Kind = RuntimeExecutionStepKind.AddPrefixRoute,
                DestinationPrefix = "test",
                Status = RuntimeExecutionStepStatus.Succeeded,
            },
            new RuntimeExecutionStepResult
            {
                StepIdentity = $"test|a|{1}",
                Kind = RuntimeExecutionStepKind.AddEndpointRoute,
                DestinationPrefix = "test",
                Status = RuntimeExecutionStepStatus.Succeeded,
            },
            new RuntimeExecutionStepResult
            {
                StepIdentity = $"test|a|{2}",
                Kind = RuntimeExecutionStepKind.RemovePrefixRoute,
                DestinationPrefix = "test",
                Status = RuntimeExecutionStepStatus.Succeeded,
            },
        ]);

        long beforeDur = durations.Count;
        long beforeOps = operations.Count;
        await h.Controller.RunCycleAsync();
        long afterDur = durations.Count;
        long afterOps = operations.Count;

        Assert.Equal(1, afterDur - beforeDur);
        Assert.Equal(1, afterOps - beforeOps);
        Assert.True(durations.Last() >= 0);
        Assert.Equal(stepCount, operations.Last());
    }

    [Fact]
    public async Task RunCycle_OneStepPlan_OperationsCountEqualsOne()
    {
        await using var h = new Harness();
        var operations = new ConcurrentQueue<double>();

        using var al = CreateActivityListener(
            new ConcurrentQueue<Activity>(), new ConcurrentQueue<Activity>());
        using var ml = CreateMeterListener(
            new ConcurrentQueue<double>(), operations);

        h.SetDecisionPlan(1);
        h.ExecutorResult = RuntimeExecutionResult.Completed(
        [
            new RuntimeExecutionStepResult
            {
                StepIdentity = "test|id",
                Kind = RuntimeExecutionStepKind.AddPrefixRoute,
                DestinationPrefix = "test",
                Status = RuntimeExecutionStepStatus.Succeeded,
            },
        ]);

        await h.Controller.RunCycleAsync();

        Assert.Single(operations);
        Assert.Equal(1, operations.Last());
    }

    [Fact]
    public async Task RunCycle_NoExecutionRequired_OutcomeNoChangeZeroOperations()
    {
        await using var h = new Harness();
        var stopped = new ConcurrentQueue<Activity>();
        var operations = new ConcurrentQueue<double>();

        // NoExecutionRequired models an empty plan reaching execution; the
        // executor is still invoked once by the orchestration, so an
        // Runtime.Execute activity with operations-per-cycle = 0 is emitted.
        using var al = CreateActivityListener(stopped, new ConcurrentQueue<Activity>());
        using var ml = CreateMeterListener(
            new ConcurrentQueue<double>(), operations);

        h.ExecutorResult = RuntimeExecutionResult.NoExecutionRequired();

        RuntimeCycleExecutionResult result = await h.Controller.RunCycleAsync();

        Assert.True(result.IsSuccess);

        Activity? execution = stopped.SingleOrDefault(a =>
            a.OperationName == IranDirectActivityNames.RuntimeExecute);
        Assert.NotNull(execution);
        Assert.Equal(
            IranDirectTagValues.NoChange,
            execution!.Tags.Single(t => t.Key == IranDirectTagNames.Outcome).Value);
        Assert.Equal(ActivityStatusCode.Ok, execution.Status);

        // RuntimeExecutor returns NoExecutionRequired for an empty plan, so the
        // authoritative O(1) plan count is 0 and recorded as such.
        Assert.Equal(0, operations.Last());
    }

    [Fact]
    public async Task RunCycle_FailedResult_OutcomeFailure()
    {
        await using var h = new Harness();
        var stopped = new ConcurrentQueue<Activity>();

        using var al = CreateActivityListener(stopped, new ConcurrentQueue<Activity>());

        h.ExecutorResult = RuntimeExecutionResult.Failed(
        [
            new RuntimeExecutionStepResult
            {
                StepIdentity = "test|id",
                Kind = RuntimeExecutionStepKind.AddPrefixRoute,
                DestinationPrefix = "test",
                Status = RuntimeExecutionStepStatus.Failed,
            },
        ], "step failed");

        RuntimeCycleExecutionResult result = await h.Controller.RunCycleAsync();

        Assert.False(result.IsSuccess);

        Activity? execution = stopped.SingleOrDefault(a =>
            a.OperationName == IranDirectActivityNames.RuntimeExecute);
        Assert.NotNull(execution);
        Assert.Equal(
            IranDirectTagValues.Failure,
            execution!.Tags.Single(t => t.Key == IranDirectTagNames.Outcome).Value);
        // Failure is represented only by result status; no free-form error text
        // becomes a failure_category.
        Assert.DoesNotContain(
            execution.Tags, t => t.Key == IranDirectTagNames.FailureCategory);
        Assert.Equal(ActivityStatusCode.Error, execution.Status);
    }

    [Fact]
    public async Task RunCycle_PartiallyCompleted_OutcomeFailure()
    {
        await using var h = new Harness();
        var stopped = new ConcurrentQueue<Activity>();

        using var al = CreateActivityListener(stopped, new ConcurrentQueue<Activity>());

        h.ExecutorResult = RuntimeExecutionResult.PartiallyCompleted(
        [
            new RuntimeExecutionStepResult
            {
                StepIdentity = "first|id",
                Kind = RuntimeExecutionStepKind.AddPrefixRoute,
                DestinationPrefix = "test",
                Status = RuntimeExecutionStepStatus.Succeeded,
            },
            new RuntimeExecutionStepResult
            {
                StepIdentity = "second|id",
                Kind = RuntimeExecutionStepKind.AddPrefixRoute,
                DestinationPrefix = "test",
                Status = RuntimeExecutionStepStatus.Failed,
            },
        ], "partial failure");

        RuntimeCycleExecutionResult result = await h.Controller.RunCycleAsync();

        Assert.False(result.IsSuccess);

        Activity? execution = stopped.SingleOrDefault(a =>
            a.OperationName == IranDirectActivityNames.RuntimeExecute);
        Assert.NotNull(execution);
        Assert.Equal(
            IranDirectTagValues.Failure,
            execution!.Tags.Single(t => t.Key == IranDirectTagNames.Outcome).Value);
        Assert.Equal(ActivityStatusCode.Error, execution.Status);
    }

    [Fact]
    public async Task RunCycle_ThrownIOException_OutcomeFailureIo()
    {
        await using var h = new Harness();
        var stopped = new ConcurrentQueue<Activity>();

        using var al = CreateActivityListener(stopped, new ConcurrentQueue<Activity>());

        h.FakeExecutor.ThrowOnExecute = new IOException("secret path C:\\x");

        await Assert.ThrowsAsync<IOException>(
            () => h.Controller.RunCycleAsync());

        Activity? execution = stopped.SingleOrDefault(a =>
            a.OperationName == IranDirectActivityNames.RuntimeExecute);
        Assert.NotNull(execution);
        Assert.Equal(
            IranDirectTagValues.Failure,
            execution!.Tags.Single(t => t.Key == IranDirectTagNames.Outcome).Value);
        Assert.Equal(
            IranDirectTagValues.FailureIo,
            execution.Tags.Single(t => t.Key == IranDirectTagNames.FailureCategory).Value);
        Assert.Equal(ActivityStatusCode.Error, execution.Status);
        foreach (var tag in execution.Tags)
            Assert.DoesNotContain("secret", tag.Value?.ToString());
    }

    [Fact]
    public async Task RunCycle_ThrownRoutingFault_OutcomeFailureRouting()
    {
        await using var h = new Harness();
        var stopped = new ConcurrentQueue<Activity>();

        using var al = CreateActivityListener(stopped, new ConcurrentQueue<Activity>());

        h.FakeExecutor.ThrowOnExecute =
            new FaultInjectionException(FaultInjectionPoint.RouteCreate);

        await Assert.ThrowsAsync<FaultInjectionException>(
            () => h.Controller.RunCycleAsync());

        Activity? execution = stopped.SingleOrDefault(a =>
            a.OperationName == IranDirectActivityNames.RuntimeExecute);
        Assert.NotNull(execution);
        Assert.Equal(
            IranDirectTagValues.FailureRouting,
            execution!.Tags.Single(t => t.Key == IranDirectTagNames.FailureCategory).Value);
    }

    [Fact]
    public async Task RunCycle_ThrownUnknownException_OutcomeFailureUnknown()
    {
        await using var h = new Harness();
        var stopped = new ConcurrentQueue<Activity>();

        using var al = CreateActivityListener(stopped, new ConcurrentQueue<Activity>());

        h.FakeExecutor.ThrowOnExecute = new InvalidOperationException("boom");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => h.Controller.RunCycleAsync());

        Activity? execution = stopped.SingleOrDefault(a =>
            a.OperationName == IranDirectActivityNames.RuntimeExecute);
        Assert.NotNull(execution);
        Assert.Equal(
            IranDirectTagValues.FailureUnknown,
            execution!.Tags.Single(t => t.Key == IranDirectTagNames.FailureCategory).Value);
    }

    [Fact]
    public async Task RunCycle_Cancelled_OutcomeCancelledNoFailureCategory()
    {
        await using var h = new Harness();
        var stopped = new ConcurrentQueue<Activity>();

        using var al = CreateActivityListener(stopped, new ConcurrentQueue<Activity>());

        h.FakeExecutor.ThrowOnExecute = new OperationCanceledException();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => h.Controller.RunCycleAsync());

        Activity? execution = stopped.SingleOrDefault(a =>
            a.OperationName == IranDirectActivityNames.RuntimeExecute);
        Assert.NotNull(execution);
        Assert.Equal(
            IranDirectTagValues.Cancelled,
            execution!.Tags.Single(t => t.Key == IranDirectTagNames.Outcome).Value);
        Assert.DoesNotContain(
            execution.Tags, t => t.Key == IranDirectTagNames.FailureCategory);
        Assert.Equal(ActivityStatusCode.Unset, execution.Status);
    }

    [Fact]
    public async Task RunCycle_NoListener_ResultUnchangedAndNoException()
    {
        await using var h = new Harness();
        h.ExecutorResult = RuntimeExecutionResult.Completed(
        [
            new RuntimeExecutionStepResult
            {
                StepIdentity = "test|id",
                Kind = RuntimeExecutionStepKind.AddPrefixRoute,
                DestinationPrefix = "test",
                Status = RuntimeExecutionStepStatus.Succeeded,
            },
        ]);

        RuntimeCycleExecutionResult result = await h.Controller.RunCycleAsync();

        Assert.True(result.IsSuccess);
        Assert.True(result.Execution.MutatedInfrastructure);
    }

    [Fact]
    public async Task RunCycle_ExecutionTelemetry_NoSensitiveTags()
    {
        await using var h = new Harness();
        var stopped = new ConcurrentQueue<Activity>();

        using var al = CreateActivityListener(stopped, new ConcurrentQueue<Activity>());

        h.ExecutorResult = RuntimeExecutionResult.Completed(
        [
            new RuntimeExecutionStepResult
            {
                StepIdentity = "test|id",
                Kind = RuntimeExecutionStepKind.AddPrefixRoute,
                DestinationPrefix = "test",
                Status = RuntimeExecutionStepStatus.Succeeded,
            },
        ]);

        await h.Controller.RunCycleAsync();

        Activity? execution = stopped.SingleOrDefault(a =>
            a.OperationName == IranDirectActivityNames.RuntimeExecute);
        Assert.NotNull(execution);

        foreach (var tag in execution!.Tags)
        {
            Assert.DoesNotContain(
                tag.Key,
                new[]
                {
                    "destination_prefix", "gateway", "next_hop",
                    "interface_index", "interface_name", "route_identity",
                    "execution_step_identity", "command", "diagnostic_id",
                    "domain", "url", "file_path", "exception_message",
                });
        }

        // Only approved tag names are present.
        foreach (var tag in execution.Tags)
        {
            Assert.Contains(
                tag.Key,
                new[]
                {
                    IranDirectTagNames.Operation,
                    IranDirectTagNames.Outcome,
                    IranDirectTagNames.FailureCategory,
                });
        }
    }

    // Proves the required runtime-cycle child-span hierarchy end-to-end through
    // the REAL production flow: the controller's RunCycleAsync starts the
    // IranDirect.RuntimeCycle root, then its RuntimeDecisionBuilder invokes the
    // REAL RuntimeReconciler (emitting Runtime.PlanChanges), and only after that
    // returns does the controller wrap ExecuteAsync with the REAL
    // Runtime.Execute producer. Because the planning scope is fully disposed
    // inside ReconcileAsync before execution begins, the two are SIBLINGS under
    // the cycle, never nested.
    [Fact]
    public async Task SiblingOfPlanning_UnderRuntimeCycle()
    {
        await using var h = new Harness(useRealReconciler: true);
        var started = new ConcurrentQueue<Activity>();
        var stopped = new ConcurrentQueue<Activity>();
        using var al = CreateActivityListener(stopped, started);

        h.ExecutorResult = RuntimeExecutionResult.NoExecutionRequired();

        await h.Controller.RunCycleAsync();

        Activity? cycle = stopped.SingleOrDefault(a =>
            a.OperationName == IranDirectActivityNames.RuntimeCycle);
        Activity? planning = stopped.SingleOrDefault(a =>
            a.OperationName == IranDirectActivityNames.RuntimePlanChanges);
        Activity? execution = stopped.SingleOrDefault(a =>
            a.OperationName == IranDirectActivityNames.RuntimeExecute);

        Assert.NotNull(cycle);
        Assert.NotNull(planning);
        Assert.NotNull(execution);

        // Same trace, both direct children of the cycle.
        Assert.Equal(cycle!.TraceId, planning!.TraceId);
        Assert.Equal(cycle.TraceId, execution!.TraceId);
        Assert.Equal(cycle.SpanId, planning.ParentSpanId);
        Assert.Equal(cycle.SpanId, execution.ParentSpanId);

        // Execution is NOT a child of planning.
        Assert.NotEqual(planning.SpanId, execution.ParentSpanId);

        // Event order proves planning fully stops before execution starts:
        // cycle -> planChanges -> cycle[still open] -> execute -> ... -> cycle.
        Activity[] startedByTrace =
            [.. started.Where(a => a.TraceId == cycle.TraceId)];
        Activity[] stoppedByTrace =
            [.. stopped.Where(a => a.TraceId == cycle.TraceId)];

        // Exactly the three expected child spans under this trace.
        Assert.Equal(3, startedByTrace.Length);
        Assert.Equal(3, stoppedByTrace.Length);
        Assert.Contains(
            startedByTrace, a => a.OperationName == IranDirectActivityNames.RuntimeCycle);
        Assert.Contains(
            startedByTrace, a => a.OperationName == IranDirectActivityNames.RuntimePlanChanges);
        Assert.Contains(
            startedByTrace, a => a.OperationName == IranDirectActivityNames.RuntimeExecute);

        // Strict sibling ordering: planning must fully stop before execution
        // starts. There must be no overlap (no nesting).
        DateTimeOffset planStop = planning.StartTimeUtc + planning.Duration;
        DateTimeOffset execStart = execution.StartTimeUtc;
        Assert.True(
            planStop <= execStart,
            $"Runtime.PlanChanges stopped at {planStop:O} but " +
            $"Runtime.Execute started at {execStart:O}; planning must stop " +
            "before execution begins (siblings, not nested).");
    }

    private sealed class StubOwnershipSource : IRuntimeRouteOwnershipSource
    {
        public Task<IReadOnlyCollection<string>>
            LoadEndpointRouteIdentitiesAsync(
                CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyCollection<string>>([]);

        public Task<IReadOnlyCollection<string>>
            LoadPrefixRouteIdentitiesAsync(
                CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyCollection<string>>([]);
    }

    private sealed class StubPlanCoordinator : IRuntimePlanCoordinator
    {
        private readonly RuntimePlanSnapshot _plan;

        public StubPlanCoordinator(RuntimePlanSnapshot plan) => _plan = plan;

        public Task<RuntimePlanSnapshot> BuildPlanAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_plan);
    }
}
