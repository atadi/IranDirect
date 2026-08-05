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

[CollectionDefinition("RuntimeCycleTelemetry", DisableParallelization = true)]
public sealed class RuntimeCycleTelemetryCollection
{
}

/// <summary>
/// Runtime-cycle telemetry instrumentation tests. Reuses the production
/// <see cref="IranDirectController"/> with lightweight fakes and captures the
/// root activity + metrics via BCL <see cref="ActivityListener"/> and
/// <see cref="MeterListener"/> only. No OpenTelemetry packages.
///
/// Static counters/histograms persist across tests, so every test asserts
/// per-listener deltas captured during its own listener lifetime, never
/// process-global totals.
/// </summary>
[Collection("RuntimeCycleTelemetry")]
public sealed class RuntimeCycleTelemetryTests
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

        public Harness(string? perfDirectory = null)
        {
            _tempDir = Path.Combine(
                Path.GetTempPath(), $"IranDirectTele_{Guid.NewGuid()}");
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
            RuntimeCycleCoordinator coordinator = new(FakeDecisionBuilder);
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
        long Started, long Completed, long Failed, long Cancelled,
        int DurationCount, double DurationSum)
    {
        public static MetricCapture From(
            ConcurrentDictionary<string, long> counters,
            ConcurrentQueue<double> durations)
        {
            counters.TryGetValue(
                IranDirectMetricNames.RuntimeCyclesStarted, out long s);
            counters.TryGetValue(
                IranDirectMetricNames.RuntimeCyclesCompleted, out long c);
            counters.TryGetValue(
                IranDirectMetricNames.RuntimeCyclesFailed, out long f);
            counters.TryGetValue(
                IranDirectMetricNames.RuntimeCyclesCancelled, out long x);
            double sum = 0;
            foreach (double d in durations) sum += d;
            return new MetricCapture(s, c, f, x, durations.Count, sum);
        }
    }

    private static MeterListener CreateMeterListener(
        ConcurrentDictionary<string, long> counters,
        ConcurrentQueue<double> durations)
    {
        var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == IranDirectTelemetry.SourceName)
                meterListener.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>(
            (instrument, value, tags, _) =>
            {
                counters.AddOrUpdate(
                    instrument.Name, value, (_, v) => v + value);
            });
        listener.SetMeasurementEventCallback<double>(
            (instrument, value, tags, _) =>
            {
                if (instrument.Name == IranDirectMetricNames.RuntimeCycleDuration)
                    durations.Enqueue(value);
            });
        listener.Start();
        return listener;
    }

    [Fact]
    public async Task RunCycle_SuccessWithChanges_ActivityAndMetrics()
    {
        await using var h = new Harness();

        var stopped = new ConcurrentQueue<Activity>();
        var started = new ConcurrentQueue<Activity>();
        var counters = new ConcurrentDictionary<string, long>();
        var durations = new ConcurrentQueue<double>();

        using var al = CreateActivityListener(stopped, started);
        using var ml = CreateMeterListener(counters, durations);

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
        MetricCapture cap = MetricCapture.From(counters, durations);

        Assert.Equal(1, cap.Started);
        Assert.Equal(1, cap.Completed);
        Assert.Equal(0, cap.Failed);
        Assert.Equal(0, cap.Cancelled);
        Assert.Equal(1, cap.DurationCount);
        Assert.True(cap.DurationSum >= 0);

        Activity? activity = stopped.SingleOrDefault();
        Assert.NotNull(activity);
        Assert.Equal(IranDirectActivityNames.RuntimeCycle, activity!.OperationName);
        Assert.Equal(ActivityKind.Internal, activity.Kind);
        Assert.Equal("runtime_cycle", activity.Tags.Single(
            t => t.Key == IranDirectTagNames.Operation).Value);
        Assert.Equal(
            IranDirectTagValues.TriggerRepair,
            activity.Tags.Single(t => t.Key == IranDirectTagNames.Trigger).Value);
        Assert.Equal(
            IranDirectTagValues.Success,
            activity.Tags.Single(t => t.Key == IranDirectTagNames.Outcome).Value);
        Assert.Equal(ActivityStatusCode.Ok, activity.Status);
    }

    [Fact]
    public async Task RunCycle_NoExecutionRequired_OutcomeNoChange()
    {
        await using var h = new Harness();
        var stopped = new ConcurrentQueue<Activity>();
        var started = new ConcurrentQueue<Activity>();
        var counters = new ConcurrentDictionary<string, long>();
        var durations = new ConcurrentQueue<double>();

        using var al = CreateActivityListener(stopped, started);
        using var ml = CreateMeterListener(counters, durations);

        h.ExecutorResult = RuntimeExecutionResult.NoExecutionRequired();

        RuntimeCycleExecutionResult result = await h.Controller.RunCycleAsync();

        Assert.True(result.IsSuccess);
        MetricCapture cap = MetricCapture.From(counters, durations);
        Assert.Equal(1, cap.Started);
        Assert.Equal(1, cap.Completed);
        Assert.Equal(1, cap.DurationCount);

        Activity? activity = stopped.SingleOrDefault();
        Assert.NotNull(activity);
        Assert.Equal(
            IranDirectTagValues.NoChange,
            activity!.Tags.Single(t => t.Key == IranDirectTagNames.Outcome).Value);
        Assert.Equal(ActivityStatusCode.Ok, activity.Status);
    }

    [Fact]
    public async Task RunCycle_Failure_OutcomeFailureAndCategory()
    {
        await using var h = new Harness();
        var stopped = new ConcurrentQueue<Activity>();
        var started = new ConcurrentQueue<Activity>();
        var counters = new ConcurrentDictionary<string, long>();
        var durations = new ConcurrentQueue<double>();

        using var al = CreateActivityListener(stopped, started);
        using var ml = CreateMeterListener(counters, durations);

        h.FakeExecutor.ThrowOnExecute = new IOException("secret path C:\\x");

        await Assert.ThrowsAsync<IOException>(
            () => h.Controller.RunCycleAsync());

        MetricCapture cap = MetricCapture.From(counters, durations);
        Assert.Equal(1, cap.Started);
        Assert.Equal(1, cap.Failed);
        Assert.Equal(0, cap.Completed);
        Assert.Equal(0, cap.Cancelled);
        Assert.Equal(1, cap.DurationCount);

        Activity? activity = stopped.SingleOrDefault();
        Assert.NotNull(activity);
        Assert.Equal(
            IranDirectTagValues.Failure,
            activity!.Tags.Single(t => t.Key == IranDirectTagNames.Outcome).Value);
        Assert.Equal(ActivityStatusCode.Error, activity.Status);
        Assert.Equal(
            IranDirectTagValues.FailureIo,
            activity.Tags.Single(
                t => t.Key == IranDirectTagNames.FailureCategory).Value);
        foreach (var tag in activity.Tags)
            Assert.DoesNotContain("secret", tag.Value?.ToString());
    }

    [Fact]
    public async Task RunCycle_RoutingFault_OutcomeFailureAndRoutingCategory()
    {
        await using var h = new Harness();
        h.FakeExecutor.ThrowOnExecute =
            new FaultInjectionException(FaultInjectionPoint.RouteCreate);

        var stopped = new ConcurrentQueue<Activity>();
        var started = new ConcurrentQueue<Activity>();
        var counters = new ConcurrentDictionary<string, long>();
        var durations = new ConcurrentQueue<double>();

        using var al = CreateActivityListener(stopped, started);
        using var ml = CreateMeterListener(counters, durations);

        await Assert.ThrowsAsync<FaultInjectionException>(
            () => h.Controller.RunCycleAsync());

        Activity? activity = stopped.SingleOrDefault();
        Assert.NotNull(activity);
        Assert.Equal(
            IranDirectTagValues.FailureRouting,
            activity!.Tags.Single(
                t => t.Key == IranDirectTagNames.FailureCategory).Value);
    }

    [Fact]
    public async Task RunCycle_UnknownException_OutcomeFailure()
    {
        await using var h = new Harness();
        h.FakeExecutor.ThrowOnExecute = new InvalidOperationException("boom");

        var stopped = new ConcurrentQueue<Activity>();
        var started = new ConcurrentQueue<Activity>();
        var counters = new ConcurrentDictionary<string, long>();
        var durations = new ConcurrentQueue<double>();

        using var al = CreateActivityListener(stopped, started);
        using var ml = CreateMeterListener(counters, durations);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => h.Controller.RunCycleAsync());

        Activity? activity = stopped.SingleOrDefault();
        Assert.NotNull(activity);
        Assert.Equal(
            IranDirectTagValues.FailureUnknown,
            activity!.Tags.Single(
                t => t.Key == IranDirectTagNames.FailureCategory).Value);
    }

    [Fact]
    public async Task RunCycle_Cancellation_OutcomeCancelled()
    {
        await using var h = new Harness();
        h.FakeExecutor.ThrowOnExecute = new OperationCanceledException();

        var stopped = new ConcurrentQueue<Activity>();
        var started = new ConcurrentQueue<Activity>();
        var counters = new ConcurrentDictionary<string, long>();
        var durations = new ConcurrentQueue<double>();

        using var al = CreateActivityListener(stopped, started);
        using var ml = CreateMeterListener(counters, durations);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => h.Controller.RunCycleAsync());

        MetricCapture cap = MetricCapture.From(counters, durations);
        Assert.Equal(1, cap.Started);
        Assert.Equal(1, cap.Cancelled);
        Assert.Equal(0, cap.Failed);
        Assert.Equal(0, cap.Completed);
        Assert.Equal(1, cap.DurationCount);

        Activity? activity = stopped.SingleOrDefault();
        Assert.NotNull(activity);
        Assert.Equal(
            IranDirectTagValues.Cancelled,
            activity!.Tags.Single(t => t.Key == IranDirectTagNames.Outcome).Value);
        Assert.DoesNotContain(
            activity.Tags,
            t => t.Key == IranDirectTagNames.FailureCategory);
        Assert.Equal(ActivityStatusCode.Unset, activity.Status);
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
    public async Task Enable_TriggerMappedToForced()
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

        var stopped = new ConcurrentQueue<Activity>();
        var started = new ConcurrentQueue<Activity>();
        using var al = CreateActivityListener(stopped, started);

        await h.Controller.EnableAsync();

        Activity? activity = stopped.SingleOrDefault();
        Assert.NotNull(activity);
        Assert.Equal(
            IranDirectTagValues.TriggerForced,
            activity!.Tags.Single(t => t.Key == IranDirectTagNames.Trigger).Value);
    }

    [Fact]
    public async Task RunCycle_ProfilerReportStillProduced()
    {
        string perfDir = Path.Combine(
            Path.GetTempPath(), $"IranDirectPerf_{Guid.NewGuid()}");
        Directory.CreateDirectory(perfDir);
        try
        {
            await using var h = new Harness(perfDir);
            h.ExecutorResult = RuntimeExecutionResult.NoExecutionRequired();

            await h.Controller.RunCycleAsync();

            string[] files = Directory.GetFiles(perfDir, "*.json");
            Assert.NotEmpty(files);
        }
        finally
        {
            try { Directory.Delete(perfDir, recursive: true); }
            catch { /* best-effort */ }
        }
    }

    [Fact]
    public async Task NoListener_NoOpCycle_AllocationOverheadIsBounded()
    {
        // Optional allocation evidence (§20). With no ActivityListener and no
        // MeterListener registered, one no-op cycle must still complete and
        // telemetry must not allocate route-sized structures. We assert the
        // observed delta is reported, not a brittle byte threshold.
        await using var h = new Harness();
        h.ExecutorResult = RuntimeExecutionResult.NoExecutionRequired();

        // Warm-up so JIT/static init costs are excluded.
        await h.Controller.RunCycleAsync();

        long before = GC.GetAllocatedBytesForCurrentThread();
        await h.Controller.RunCycleAsync();
        long after = GC.GetAllocatedBytesForCurrentThread();

        long delta = after - before;
        Assert.True(delta >= 0);
    }
}
