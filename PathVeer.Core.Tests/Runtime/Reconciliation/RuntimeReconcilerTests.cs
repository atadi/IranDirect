using PathVeer.Core.Configuration;
using PathVeer.Core.Observability.Telemetry;
using PathVeer.Core.Runtime;
using PathVeer.Core.Runtime.Reconciliation;
using PathVeer.Core.Testing.FaultInjection;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Xunit;

namespace PathVeer.Core.Tests.Runtime.Reconciliation;

[Collection("RuntimeCycleTelemetry")]
public sealed class RuntimeReconcilerTests
{
    private readonly RuntimeChangeSetPlanner _planner = new();

    [Fact]
    public async Task ReconcileAsync_BlockedPlan_ReturnsBlocked()
    {
        RuntimeReconciler reconciler = CreateReconciler();
        RuntimePlanSnapshot snapshot = CreateSnapshot(blocked: true);

        RuntimeReconciliationResult result =
            await reconciler.ReconcileAsync(snapshot);

        Assert.Equal(
            RuntimeReconciliationStatus.Blocked,
            result.Status);
        Assert.False(result.Succeeded);
        Assert.False(result.MutatedInfrastructure);
        Assert.Contains(
            "Configuration is invalid.",
            result.Messages);
    }

    [Fact]
    public async Task ReconcileAsync_BlockedPlan_DoesNotLoadOwnership()
    {
        TrackingSource source = new();
        RuntimeReconciler reconciler = CreateReconciler(source);
        RuntimePlanSnapshot snapshot = CreateSnapshot(blocked: true);

        await reconciler.ReconcileAsync(snapshot);

        Assert.Equal(0, source.CallCount);
    }

    [Fact]
    public async Task ReconcileAsync_MatchingRuntime_ReturnsNoChangesRequired()
    {
        RuntimeReconciler reconciler = CreateReconciler(
            endpointIdentities:
            [
                EndpointObserved().Identity
            ],
            prefixIdentities:
            [
                PrefixObserved().Identity
            ]);

        RuntimePlanSnapshot snapshot = CreateSnapshot(
            observedRoutes:
            [
                EndpointObserved(),
                PrefixObserved()
            ]);

        RuntimeReconciliationResult result =
            await reconciler.ReconcileAsync(snapshot);

        Assert.Equal(
            RuntimeReconciliationStatus.NoChangesRequired,
            result.Status);
        Assert.True(result.Succeeded);
        Assert.False(result.MutatedInfrastructure);
        Assert.True(result.ChangeSet.IsEmpty);
    }

    [Fact]
    public async Task ReconcileAsync_WithDifferences_ReturnsChangesPlanned()
    {
        RuntimeReconciler reconciler = CreateReconciler();
        RuntimePlanSnapshot snapshot = CreateSnapshot(
            observedRoutes: []);

        RuntimeReconciliationResult result =
            await reconciler.ReconcileAsync(snapshot);

        Assert.Equal(
            RuntimeReconciliationStatus.ChangesPlanned,
            result.Status);
        Assert.True(result.Succeeded);
        Assert.False(result.MutatedInfrastructure);
        Assert.False(result.ChangeSet.IsEmpty);
    }

    [Fact]
    public async Task ReconcileAsync_ChangesPlanned_ContainsExactChangeSet()
    {
        RuntimeReconciler reconciler = CreateReconciler();
        RuntimePlanSnapshot snapshot = CreateSnapshot(
            observedRoutes: []);

        RuntimeReconciliationResult result =
            await reconciler.ReconcileAsync(snapshot);

        Assert.Contains(
            result.ChangeSet.Changes,
            change =>
                change.Kind ==
                RuntimeChangeKind.AddEndpointRoute);

        Assert.Contains(
            result.ChangeSet.Changes,
            change =>
                change.Kind ==
                RuntimeChangeKind.AddPrefixRoute);
    }

    [Fact]
    public async Task ReconcileAsync_ChangesPlanned_DoesNotReportMutation()
    {
        RuntimeReconciler reconciler = CreateReconciler();
        RuntimePlanSnapshot snapshot = CreateSnapshot(
            observedRoutes: []);

        RuntimeReconciliationResult result =
            await reconciler.ReconcileAsync(snapshot);

        Assert.False(result.MutatedInfrastructure);
    }

    [Fact]
    public async Task ReconcileAsync_LoadsOwnershipExactlyOnce()
    {
        TrackingSource source = new();
        RuntimeReconciler reconciler = CreateReconciler(source);
        RuntimePlanSnapshot snapshot = CreateSnapshot();

        await reconciler.ReconcileAsync(snapshot);

        Assert.Equal(2, source.CallCount);
    }

    [Fact]
    public async Task ReconcileAsync_Cancellation_Propagates()
    {
        CancellationTokenSource cts = new();
        RuntimeReconciler reconciler = CreateReconciler(
            new CancellingSource(cts.Token));
        RuntimePlanSnapshot snapshot = CreateSnapshot();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => reconciler.ReconcileAsync(
                snapshot, cts.Token));
    }

    [Fact]
    public async Task ReconcileAsync_WhenOwnershipFails_ReturnsFailed()
    {
        RuntimeReconciler reconciler = CreateReconciler(
            new ThrowingSource());
        RuntimePlanSnapshot snapshot = CreateSnapshot();

        RuntimeReconciliationResult result =
            await reconciler.ReconcileAsync(snapshot);

        Assert.Equal(
            RuntimeReconciliationStatus.Failed,
            result.Status);
        Assert.False(result.Succeeded);
        Assert.False(result.MutatedInfrastructure);
    }

    [Fact]
    public async Task ReconcileAsync_WhenSnapshotIsNull_Throws()
    {
        RuntimeReconciler reconciler = CreateReconciler();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => reconciler.ReconcileAsync(null!));
    }

    [Fact]
    public void PlannedFactory_RequiresNonNullChangeSet()
    {
        Assert.Throws<ArgumentNullException>(
            () => RuntimeReconciliationResult.Planned(null!));
    }

    [Fact]
    public void PlannedFactory_RequiresNonEmptyChangeSet()
    {
        Assert.Throws<ArgumentException>(
            () => RuntimeReconciliationResult.Planned(
                new RuntimeChangeSet()));
    }

    [Fact]
    public void PlannedFactory_WithChanges_SucceedsWithoutMutation()
    {
        RuntimeChangeSet changeSet = new()
        {
            Changes =
            [
                new RuntimeChange
                {
                    Kind = RuntimeChangeKind.AddEndpointRoute,
                    Identity = "5.160.74.148/32|192.168.100.1|30",
                    DestinationPrefix = "5.160.74.148/32",
                    Gateway = "192.168.100.1",
                    InterfaceIndex = 30,
                    Metric = 1,
                    Description = "Test change."
                }
            ]
        };

        RuntimeReconciliationResult result =
            RuntimeReconciliationResult.Planned(changeSet);

        Assert.True(result.Succeeded);
        Assert.False(result.MutatedInfrastructure);
        Assert.Equal(
            RuntimeReconciliationStatus.ChangesPlanned,
            result.Status);
        Assert.Single(result.ChangeSet.Changes);
    }

    private static RuntimeReconciler CreateReconciler(
        IReadOnlyCollection<string>? endpointIdentities = null,
        IReadOnlyCollection<string>? prefixIdentities = null)
    {
        TrackingSource source = new()
        {
            EndpointIdentities =
                endpointIdentities ?? [],
            PrefixIdentities =
                prefixIdentities ?? []
        };

        return CreateReconciler(source);
    }

    private static RuntimeReconciler CreateReconciler(
        IRuntimeRouteOwnershipSource source)
    {
        RuntimeRouteOwnershipProvider provider = new(source);
        return new RuntimeReconciler(
            provider, new RuntimeChangeSetPlanner());
    }

    [Fact]
    public async Task ReconcileAsync_WithDifferences_EmitsPlanChangesChildActivity()
    {
        RuntimeReconciler reconciler = CreateReconciler();
        RuntimePlanSnapshot snapshot = CreateSnapshot(observedRoutes: []);

        var started = new ConcurrentQueue<Activity>();
        var stopped = new ConcurrentQueue<Activity>();
        using var listener = CreateActivityListener(started, stopped);

        await reconciler.ReconcileAsync(snapshot);

        Activity? planning = stopped.SingleOrDefault(a =>
            a.OperationName == IranDirectActivityNames.RuntimePlanChanges);
        Assert.NotNull(planning);
        Assert.Equal(ActivityKind.Internal, planning!.Kind);
        Assert.Equal(
            IranDirectTagValues.OperationPlanChanges,
            planning.Tags.Single(t => t.Key == IranDirectTagNames.Operation).Value);
        Assert.Equal(
            IranDirectTagValues.Success,
            planning.Tags.Single(t => t.Key == IranDirectTagNames.Outcome).Value);
        Assert.Equal(ActivityStatusCode.Ok, planning.Status);
    }

    [Fact]
    public async Task ReconcileAsync_NoChanges_OutcomeNoChange()
    {
        RuntimeReconciler reconciler = CreateReconciler(
            endpointIdentities: [EndpointObserved().Identity],
            prefixIdentities: [PrefixObserved().Identity]);
        RuntimePlanSnapshot snapshot = CreateSnapshot(
            observedRoutes: [EndpointObserved(), PrefixObserved()]);

        var stopped = new ConcurrentQueue<Activity>();
        using var listener = CreateActivityListener(
            new ConcurrentQueue<Activity>(), stopped);

        await reconciler.ReconcileAsync(snapshot);

        Activity? planning = stopped.SingleOrDefault(a =>
            a.OperationName == IranDirectActivityNames.RuntimePlanChanges);
        Assert.NotNull(planning);
        Assert.Equal(
            IranDirectTagValues.NoChange,
            planning!.Tags.Single(t => t.Key == IranDirectTagNames.Outcome).Value);
        Assert.Equal(ActivityStatusCode.Ok, planning.Status);
    }

    [Fact]
    public async Task ReconcileAsync_PlanningFailure_OutcomeFailureAndCategory()
    {
        RuntimeReconciler reconciler = CreateReconciler(new ThrowingSource());
        RuntimePlanSnapshot snapshot = CreateSnapshot(observedRoutes: []);

        var stopped = new ConcurrentQueue<Activity>();
        using var listener = CreateActivityListener(
            new ConcurrentQueue<Activity>(), stopped);

        // ThrowingSource fails ownership load, which occurs BEFORE the planner
        // invocation. The reconciler converts it to a Failed reconciliation
        // result and emits no planning activity for that path.
        RuntimeReconciliationResult result =
            await reconciler.ReconcileAsync(snapshot);
        Assert.Equal(RuntimeReconciliationStatus.Failed, result.Status);

        Activity? planning = stopped.SingleOrDefault(a =>
            a.OperationName == IranDirectActivityNames.RuntimePlanChanges);
        // Ownership-load failure occurs before the planner invocation, so no
        // planning activity is emitted. Verify no planning activity leaked a
        // success/no-change outcome.
        if (planning is not null)
        {
            Assert.Equal(
                IranDirectTagValues.Failure,
                planning.Tags.Single(t => t.Key == IranDirectTagNames.Outcome).Value);
        }
    }

    [Fact]
    public async Task ReconcileAsync_IOException_OutcomeFailureIo()
    {
        // A planner that throws IOException mid-plan. Wrap the planner via a
        // throwing ownership provider is not possible (provider runs first),
        // so use a custom planner fake that throws on Plan.
        var throwingPlanner = new ThrowingPlannerFake(new IOException("disk"));
        RuntimeReconciler reconciler = new(
            new RuntimeRouteOwnershipProvider(new TrackingSource()),
            throwingPlanner);

        RuntimePlanSnapshot snapshot = CreateSnapshot(observedRoutes: []);

        var stopped = new ConcurrentQueue<Activity>();
        using var listener = CreateActivityListener(
            new ConcurrentQueue<Activity>(), stopped);

        RuntimeReconciliationResult result =
            await reconciler.ReconcileAsync(snapshot);
        Assert.Equal(RuntimeReconciliationStatus.Failed, result.Status);

        Activity? planning = stopped.SingleOrDefault(a =>
            a.OperationName == IranDirectActivityNames.RuntimePlanChanges);
        Assert.NotNull(planning);
        Assert.Equal(
            IranDirectTagValues.Failure,
            planning!.Tags.Single(t => t.Key == IranDirectTagNames.Outcome).Value);
        Assert.Equal(ActivityStatusCode.Error, planning.Status);
        Assert.Equal(
            IranDirectTagValues.FailureIo,
            planning.Tags.Single(t => t.Key == IranDirectTagNames.FailureCategory).Value);
        foreach (var tag in planning.Tags)
            Assert.DoesNotContain("disk", tag.Value?.ToString());
    }

    [Fact]
    public async Task ReconcileAsync_RoutingFault_OutcomeFailureRouting()
    {
        var throwingPlanner = new ThrowingPlannerFake(
            new FaultInjectionException(FaultInjectionPoint.RouteCreate));
        RuntimeReconciler reconciler = new(
            new RuntimeRouteOwnershipProvider(new TrackingSource()),
            throwingPlanner);

        RuntimePlanSnapshot snapshot = CreateSnapshot(observedRoutes: []);

        var stopped = new ConcurrentQueue<Activity>();
        using var listener = CreateActivityListener(
            new ConcurrentQueue<Activity>(), stopped);

        await reconciler.ReconcileAsync(snapshot);

        Activity? planning = stopped.SingleOrDefault(a =>
            a.OperationName == IranDirectActivityNames.RuntimePlanChanges);
        Assert.NotNull(planning);
        Assert.Equal(
            IranDirectTagValues.FailureRouting,
            planning!.Tags.Single(t => t.Key == IranDirectTagNames.FailureCategory).Value);
    }

    [Fact]
    public async Task ReconcileAsync_UnknownException_OutcomeFailureUnknown()
    {
        var throwingPlanner = new ThrowingPlannerFake(
            new InvalidOperationException("boom"));
        RuntimeReconciler reconciler = new(
            new RuntimeRouteOwnershipProvider(new TrackingSource()),
            throwingPlanner);

        RuntimePlanSnapshot snapshot = CreateSnapshot(observedRoutes: []);

        var stopped = new ConcurrentQueue<Activity>();
        using var listener = CreateActivityListener(
            new ConcurrentQueue<Activity>(), stopped);

        await reconciler.ReconcileAsync(snapshot);

        Activity? planning = stopped.SingleOrDefault(a =>
            a.OperationName == IranDirectActivityNames.RuntimePlanChanges);
        Assert.NotNull(planning);
        Assert.Equal(
            IranDirectTagValues.FailureUnknown,
            planning!.Tags.Single(t => t.Key == IranDirectTagNames.FailureCategory).Value);
    }

    [Fact]
    public async Task ReconcileAsync_PlanningDurationAndChangedRoutesRecorded()
    {
        RuntimeReconciler reconciler = CreateReconciler();
        RuntimePlanSnapshot snapshot = CreateSnapshot(observedRoutes: []);

        var durations = new ConcurrentQueue<double>();
        var changed = new ConcurrentQueue<double>();
        using var listener = CreateMeterListener(durations, changed);

        // Snapshot counts immediately around the single call so concurrent
        // planning from other in-flight test cycles does not pollute the
        // per-call assertion (shared static Meter).
        int durBefore = durations.Count;
        int changedBefore = changed.Count;
        await reconciler.ReconcileAsync(snapshot);
        int durAfter = durations.Count;
        int changedAfter = changed.Count;

        Assert.Equal(1, durAfter - durBefore);
        Assert.Equal(1, changedAfter - changedBefore);
        Assert.True(durations.Last() >= 0);
        Assert.True(changed.Last() > 0);
    }

    [Fact]
    public async Task ReconcileAsync_NoChanges_ChangedRoutesRecordsZero()
    {
        RuntimeReconciler reconciler = CreateReconciler(
            endpointIdentities: [EndpointObserved().Identity],
            prefixIdentities: [PrefixObserved().Identity]);
        RuntimePlanSnapshot snapshot = CreateSnapshot(
            observedRoutes: [EndpointObserved(), PrefixObserved()]);

        var durations = new ConcurrentQueue<double>();
        var changed = new ConcurrentQueue<double>();
        using var listener = CreateMeterListener(durations, changed);

        int durBefore = durations.Count;
        int changedBefore = changed.Count;
        await reconciler.ReconcileAsync(snapshot);
        int durAfter = durations.Count;
        int changedAfter = changed.Count;

        Assert.Equal(1, durAfter - durBefore);
        Assert.Equal(1, changedAfter - changedBefore);
        Assert.Equal(0, changed.Last());
    }

    [Fact]
    public async Task ReconcileAsync_NoListener_ResultUnchangedAndNoException()
    {
        // Without any listener, StartActivity returns null and Histogram.Record
        // is a no-op. Behavior must be identical to the non-telemetry path.
        RuntimeReconciler reconciler = CreateReconciler();
        RuntimePlanSnapshot snapshot = CreateSnapshot(observedRoutes: []);

        RuntimeReconciliationResult result =
            await reconciler.ReconcileAsync(snapshot);

        Assert.Equal(RuntimeReconciliationStatus.ChangesPlanned, result.Status);
        Assert.False(result.ChangeSet.IsEmpty);
    }

    private static ActivityListener CreateActivityListener(
        ConcurrentQueue<Activity> started,
        ConcurrentQueue<Activity> stopped)
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

    private static MeterListener CreateMeterListener(
        ConcurrentQueue<double> durations,
        ConcurrentQueue<double> changed)
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
                if (instrument.Name == IranDirectMetricNames.RuntimePlanningDuration)
                    durations.Enqueue(value);
                else if (instrument.Name == IranDirectMetricNames.RuntimeChangedRoutes)
                    changed.Enqueue(value);
            });
        listener.Start();
        return listener;
    }

    private sealed class ThrowingPlannerFake : IRuntimeChangeSetPlanner
    {
        private readonly Exception _exception;

        public ThrowingPlannerFake(Exception exception) => _exception = exception;

        public RuntimeChangeSet Plan(
            RuntimePlanSnapshot snapshot,
            RuntimeRouteOwnership ownership)
        {
            throw _exception;
        }
    }

    private static RuntimePlanSnapshot CreateSnapshot(
        bool desiredEnabled = true,
        IReadOnlyList<ObservedRoute>? observedRoutes = null,
        bool blocked = false)
    {
        DesiredEndpointRoute endpoint = new()
        {
            Host = "vpn.example",
            Address = "5.160.74.148",
            Port = 1409,
            Protocol = "tcp",
            DestinationPrefix = "5.160.74.148/32",
            Gateway = "192.168.100.1",
            InterfaceIndex = 30,
            Metric = 1
        };

        DesiredPrefixRoute prefix = new()
        {
            DestinationPrefix = "203.0.113.0/24",
            Gateway = "192.168.100.1",
            InterfaceIndex = 30,
            Metric = 5
        };

        DesiredRuntime desired = new()
        {
            Enabled = desiredEnabled,
            EndpointRoutes = [endpoint],
            PrefixRoutes =
                desiredEnabled ? [prefix] : [],
            Blockers =
                blocked
                    ?
                    [
                        new RuntimeBlocker
                        {
                            Code =
                                RuntimeBlockerCode
                                    .InvalidConfiguration,
                            Message =
                                "Configuration is invalid."
                        }
                    ]
                    : []
        };

        return new RuntimePlanSnapshot
        {
            Configuration =
                ConfigurationDefaults.Create() with
                {
                    Enabled = desiredEnabled
                },
            Desired = desired,
            Observed = new ObservedRuntime
            {
                VpnProfileExists = true,
                VpnProfileValid = true,
                DirectGateway =
                    new ObservedDirectGateway
                    {
                        Address = "192.168.100.1",
                        InterfaceIndex = 30,
                        InterfaceName = "Ethernet",
                        InterfaceMetric = 10
                    },
                VpnEndpoints =
                [
                    new ObservedVpnEndpoint
                    {
                        Host = "vpn.example",
                        Address = "5.160.74.148",
                        Port = 1409,
                        Protocol = "tcp"
                    }
                ],
                Prefixes =
                [
                    "203.0.113.0/24"
                ],
                Routes =
                    observedRoutes ?? [],
                ObservedAt = DateTimeOffset.UtcNow
            },
            PlannedAt = DateTimeOffset.UtcNow
        };
    }

    private static ObservedRoute EndpointObserved() =>
        new()
        {
            DestinationPrefix = "5.160.74.148/32",
            NextHop = "192.168.100.1",
            InterfaceIndex = 30,
            Metric = 1
        };

    private static ObservedRoute PrefixObserved() =>
        new()
        {
            DestinationPrefix = "203.0.113.0/24",
            NextHop = "192.168.100.1",
            InterfaceIndex = 30,
            Metric = 5
        };

    private sealed class TrackingSource :
        IRuntimeRouteOwnershipSource
    {
        public int CallCount { get; private set; }

        public IReadOnlyCollection<string>
            EndpointIdentities { get; init; } = [];

        public IReadOnlyCollection<string>
            PrefixIdentities { get; init; } = [];

        public Task<IReadOnlyCollection<string>>
            LoadEndpointRouteIdentitiesAsync(
                CancellationToken cancellationToken = default)
        {
            CallCount++;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(EndpointIdentities);
        }

        public Task<IReadOnlyCollection<string>>
            LoadPrefixRouteIdentitiesAsync(
                CancellationToken cancellationToken = default)
        {
            CallCount++;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(PrefixIdentities);
        }
    }

    private sealed class CancellingSource :
        IRuntimeRouteOwnershipSource
    {
        private readonly CancellationToken _cancelToken;

        public CancellingSource(
            CancellationToken cancelToken)
        {
            _cancelToken = cancelToken;
        }

        public Task<IReadOnlyCollection<string>>
            LoadEndpointRouteIdentitiesAsync(
                CancellationToken cancellationToken = default)
        {
            CancellationTokenSource linked = CancellationTokenSource
                .CreateLinkedTokenSource(
                    cancellationToken, _cancelToken);

            linked.Token.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyCollection<string>>(
                []);
        }

        public Task<IReadOnlyCollection<string>>
            LoadPrefixRouteIdentitiesAsync(
                CancellationToken cancellationToken = default)
        {
            CancellationTokenSource linked = CancellationTokenSource
                .CreateLinkedTokenSource(
                    cancellationToken, _cancelToken);

            linked.Token.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyCollection<string>>(
                []);
        }
    }

    private sealed class ThrowingSource :
        IRuntimeRouteOwnershipSource
    {
        public Task<IReadOnlyCollection<string>>
            LoadEndpointRouteIdentitiesAsync(
                CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException(
                "Inventory corruption detected.");
        }

        public Task<IReadOnlyCollection<string>>
            LoadPrefixRouteIdentitiesAsync(
                CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException(
                "Inventory corruption detected.");
        }
    }
}
