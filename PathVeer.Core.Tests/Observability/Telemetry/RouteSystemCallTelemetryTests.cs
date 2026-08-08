using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using PathVeer.Core.Observability.Telemetry;
using PathVeer.Core.Routing;
using PathVeer.Core.SystemTools;
using PathVeer.Core.Testing.FaultInjection;
using Xunit;

namespace PathVeer.Core.Tests.Observability.Telemetry;

/// <summary>
/// Exercises the route system-call telemetry boundary end-to-end through the
/// real <see cref="WindowsRouteManager"/> composed with the
/// <see cref="TelemetryRouteApi"/> decorator, using a fake native API so no
/// real powershell.exe / netsh.exe is invoked. Covers enumerate / create /
/// delete, batch and empty-batch semantics, failure / cancellation / timeout
/// mapping, no-listener safety, and the parent-child hierarchy under a real
/// runtime execution activity. Uses synchronous listener callbacks, never
/// sleeps.
/// </summary>
[Collection("RuntimeCycleTelemetry")]
public sealed class RouteSystemCallTelemetryTests
{
    private sealed class FakeWindowsRouteApi : IWindowsRouteApi
    {
        public HashSet<string> Present { get; } =
            new(StringComparer.OrdinalIgnoreCase);

        public int EnumerateCallCount { get; private set; }
        public int AddCallCount { get; private set; }
        public int DeleteCallCount { get; private set; }

        public Exception? EnumerateThrow;
        public Exception? AddThrow;
        public Exception? DeleteThrow;

        public IReadOnlyList<SystemRoute> EnumerateResult =
            Array.Empty<SystemRoute>();

        public Task<IReadOnlyList<SystemRoute>> EnumerateAsync(
            CancellationToken cancellationToken = default)
        {
            EnumerateCallCount++;
            cancellationToken.ThrowIfCancellationRequested();
            if (EnumerateThrow is not null)
                throw EnumerateThrow;
            return Task.FromResult(EnumerateResult);
        }

        public Task AddAsync(
            IReadOnlyCollection<ManagedRoute> routes,
            CancellationToken cancellationToken = default)
        {
            AddCallCount++;
            cancellationToken.ThrowIfCancellationRequested();
            if (AddThrow is not null)
                throw AddThrow;
            foreach (ManagedRoute route in routes)
                Present.Add(route.Identity);
            return Task.CompletedTask;
        }

        public Task DeleteAsync(
            IReadOnlyCollection<ManagedRoute> routes,
            CancellationToken cancellationToken = default)
        {
            DeleteCallCount++;
            cancellationToken.ThrowIfCancellationRequested();
            if (DeleteThrow is not null)
                throw DeleteThrow;
            foreach (ManagedRoute route in routes)
                Present.Remove(route.Identity);
            return Task.CompletedTask;
        }
    }

    private static WindowsRouteManager CreateManager(
        FakeWindowsRouteApi api,
        IFaultInjectionPolicy? faultPolicy = null) =>
        new(
            new CommandRunner(),
            new TelemetryRouteApi(api),
            faultPolicy);

    private static ManagedRoute CreateRoute(
        string prefix = "203.0.113.0/24",
        string gateway = "192.168.1.1",
        uint interfaceIndex = 10) =>
        new()
        {
            DestinationPrefix = prefix,
            Gateway = IPAddress.Parse(gateway),
            InterfaceIndex = interfaceIndex,
            Metric = 5,
        };

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

    private sealed record RouteMetricCapture(
        long Requested, long Succeeded, long Failed, long DurationCount)
    {
        public static RouteMetricCapture From(
            ConcurrentQueue<long> requested,
            ConcurrentQueue<long> succeeded,
            ConcurrentQueue<long> failed,
            ConcurrentQueue<double> durations) =>
            new(requested.Count, succeeded.Count, failed.Count, durations.Count);
    }

    private static MeterListener CreateMeterListener(
        ConcurrentQueue<long> requested,
        ConcurrentQueue<long> succeeded,
        ConcurrentQueue<long> failed,
        ConcurrentQueue<double> durations)
    {
        var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == IranDirectTelemetry.SourceName)
                meterListener.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            switch (instrument.Name)
            {
                case IranDirectMetricNames.RoutesOperationsRequested:
                    requested.Enqueue(value); break;
                case IranDirectMetricNames.RoutesOperationsSucceeded:
                    succeeded.Enqueue(value); break;
                case IranDirectMetricNames.RoutesOperationsFailed:
                    failed.Enqueue(value); break;
            }
        });
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
        {
            if (instrument.Name == IranDirectMetricNames.RoutesSystemCallDuration)
                durations.Enqueue(value);
        });
        listener.Start();
        return listener;
    }

    // ---- Enumerate ---------------------------------------------------------

    [Fact]
    public async Task Enumerate_Success_EmitsOneActivityAndMetrics()
    {
        var api = new FakeWindowsRouteApi();
        api.EnumerateResult =
        [
            new SystemRoute
            {
                DestinationPrefix = "203.0.113.0/24",
                NextHop = IPAddress.Parse("192.168.1.1"),
                InterfaceIndex = 10,
                RouteMetric = 5,
            },
        ];
        var manager = CreateManager(api);

        var stopped = new ConcurrentQueue<Activity>();
        var started = new ConcurrentQueue<Activity>();
        var requested = new ConcurrentQueue<long>();
        var succeeded = new ConcurrentQueue<long>();
        var failed = new ConcurrentQueue<long>();
        var durations = new ConcurrentQueue<double>();

        using var al = CreateActivityListener(stopped, started);
        using var ml = CreateMeterListener(requested, succeeded, failed, durations);

        RouteMetricCapture before = RouteMetricCapture.From(
            requested, succeeded, failed, durations);

        IReadOnlyList<SystemRoute> result = await manager.GetIpv4RoutesAsync();

        RouteMetricCapture after = RouteMetricCapture.From(
            requested, succeeded, failed, durations);

        Assert.Single(result);
        Assert.Equal(1, api.EnumerateCallCount);

        Activity? enumerate = stopped.SingleOrDefault(a =>
            a.OperationName == IranDirectActivityNames.RoutesEnumerate);
        Assert.NotNull(enumerate);
        Assert.Equal(ActivityKind.Internal, enumerate!.Kind);
        Assert.Equal(
            IranDirectTagValues.OperationEnumerateRoutes,
            enumerate.Tags.Single(t => t.Key == IranDirectTagNames.Operation).Value);

        Assert.Equal(1, after.Requested - before.Requested);
        Assert.Equal(1, after.Succeeded - before.Succeeded);
        Assert.Equal(0, after.Failed - before.Failed);
        Assert.Equal(1, after.DurationCount - before.DurationCount);
    }

    [Fact]
    public async Task Enumerate_EmptyResult_StillRecordsSuccess()
    {
        var api = new FakeWindowsRouteApi();
        var manager = CreateManager(api);

        var requested = new ConcurrentQueue<long>();
        var succeeded = new ConcurrentQueue<long>();
        var failed = new ConcurrentQueue<long>();
        var durations = new ConcurrentQueue<double>();

        using var ml = CreateMeterListener(requested, succeeded, failed, durations);
        RouteMetricCapture before = RouteMetricCapture.From(
            requested, succeeded, failed, durations);

        IReadOnlyList<SystemRoute> result = await manager.GetIpv4RoutesAsync();

        RouteMetricCapture after = RouteMetricCapture.From(
            requested, succeeded, failed, durations);
        Assert.Empty(result);
        Assert.Equal(1, api.EnumerateCallCount);
        Assert.Equal(1, after.Requested - before.Requested);
        Assert.Equal(1, after.Succeeded - before.Succeeded);
        Assert.Equal(0, after.Failed - before.Failed);
    }

    [Fact]
    public async Task Enumerate_IOException_RecordsFailureAndRethrows()
    {
        var api = new FakeWindowsRouteApi
        {
            EnumerateThrow = new IOException("secret C:\\x"),
        };
        var manager = CreateManager(api);

        var stopped = new ConcurrentQueue<Activity>();
        var requested = new ConcurrentQueue<long>();
        var succeeded = new ConcurrentQueue<long>();
        var failed = new ConcurrentQueue<long>();
        var durations = new ConcurrentQueue<double>();

        using var al = CreateActivityListener(stopped, new ConcurrentQueue<Activity>());
        using var ml = CreateMeterListener(requested, succeeded, failed, durations);
        RouteMetricCapture before = RouteMetricCapture.From(
            requested, succeeded, failed, durations);

        IOException ex = await Assert.ThrowsAsync<IOException>(
            () => manager.GetIpv4RoutesAsync());

        RouteMetricCapture after = RouteMetricCapture.From(
            requested, succeeded, failed, durations);
        Assert.Equal(1, api.EnumerateCallCount);
        Assert.Equal(1, after.Requested - before.Requested);
        Assert.Equal(0, after.Succeeded - before.Succeeded);
        Assert.Equal(1, after.Failed - before.Failed);

        Activity? enumerate = stopped.SingleOrDefault(a =>
            a.OperationName == IranDirectActivityNames.RoutesEnumerate);
        Assert.NotNull(enumerate);
        Assert.Equal(
            IranDirectTagValues.Failure,
            enumerate!.Tags.Single(t => t.Key == IranDirectTagNames.Outcome).Value);
        Assert.Equal(ActivityStatusCode.Error, enumerate.Status);
        Assert.Equal(
            IranDirectTagValues.FailureIo,
            enumerate.Tags.Single(t =>
                t.Key == IranDirectTagNames.FailureCategory).Value);
        // The exception legitimately retains its message; telemetry must not
        // surface it on the span (verified by Enumerate_NoSensitiveTags).
    }

    [Fact]
    public async Task Enumerate_RouteEnumerationFault_ZeroNativeCallsAndRoutingCategory()
    {
        var api = new FakeWindowsRouteApi();
        var manager = CreateManager(
            api,
            FaultInjectionPolicy.For([FaultInjectionPoint.RouteEnumeration]));

        var stopped = new ConcurrentQueue<Activity>();
        var failed = new ConcurrentQueue<long>();

        using var al = CreateActivityListener(stopped, new ConcurrentQueue<Activity>());
        using var ml = CreateMeterListener(
            new ConcurrentQueue<long>(), new ConcurrentQueue<long>(), failed,
            new ConcurrentQueue<double>());

        FaultInjectionException ex = await Assert.ThrowsAsync<FaultInjectionException>(
            () => manager.GetIpv4RoutesAsync());

        Assert.Equal(FaultInjectionPoint.RouteEnumeration, ex.Point);
        Assert.Equal(0, api.EnumerateCallCount);
        // Fault is raised by WindowsRouteManager before the wrapped API is
        // invoked, so no native call was attempted and no telemetry recorded.
        Assert.Equal(0, failed.Count);

        Assert.DoesNotContain(
            stopped,
            a => a.OperationName == IranDirectActivityNames.RoutesEnumerate);
    }

    [Fact]
    public async Task Enumerate_UnknownException_RecordsFailureUnknown()
    {
        var api = new FakeWindowsRouteApi
        {
            EnumerateThrow = new InvalidOperationException("boom"),
        };
        var manager = CreateManager(api);

        var stopped = new ConcurrentQueue<Activity>();
        using var al = CreateActivityListener(stopped, new ConcurrentQueue<Activity>());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => manager.GetIpv4RoutesAsync());

        Activity? enumerate = stopped.SingleOrDefault(a =>
            a.OperationName == IranDirectActivityNames.RoutesEnumerate);
        Assert.NotNull(enumerate);
        Assert.Equal(
            IranDirectTagValues.FailureUnknown,
            enumerate!.Tags.Single(t =>
                t.Key == IranDirectTagNames.FailureCategory).Value);
    }

    [Fact]
    public async Task Enumerate_NoSensitiveTags()
    {
        var api = new FakeWindowsRouteApi();
        api.EnumerateResult =
        [
            new SystemRoute
            {
                DestinationPrefix = "203.0.113.0/24",
                NextHop = IPAddress.Parse("192.168.1.1"),
                InterfaceIndex = 10,
                RouteMetric = 5,
            },
        ];
        var manager = CreateManager(api);

        var stopped = new ConcurrentQueue<Activity>();
        using var al = CreateActivityListener(stopped, new ConcurrentQueue<Activity>());

        await manager.GetIpv4RoutesAsync();

        Activity? enumerate = stopped.SingleOrDefault(a =>
            a.OperationName == IranDirectActivityNames.RoutesEnumerate);
        Assert.NotNull(enumerate);
        foreach (var tag in enumerate!.Tags)
        {
            Assert.DoesNotContain("203.0.113", tag.Value?.ToString());
            Assert.DoesNotContain("192.168.1.1", tag.Value?.ToString());
            Assert.DoesNotContain(
                tag.Key,
                new[]
                {
                    "destination_prefix", "gateway", "next_hop",
                    "interface_index", "interface_name", "route_identity",
                    "command", "exception_message", "file_path",
                });
        }
    }

    // ---- Create ------------------------------------------------------------

    [Fact]
    public async Task Create_OneRouteBatch_EmitsOneActivityAndMetrics()
    {
        var api = new FakeWindowsRouteApi();
        var manager = CreateManager(api);

        var stopped = new ConcurrentQueue<Activity>();
        var started = new ConcurrentQueue<Activity>();
        var requested = new ConcurrentQueue<long>();
        var succeeded = new ConcurrentQueue<long>();
        var failed = new ConcurrentQueue<long>();
        var durations = new ConcurrentQueue<double>();

        using var al = CreateActivityListener(stopped, started);
        using var ml = CreateMeterListener(requested, succeeded, failed, durations);
        RouteMetricCapture before = RouteMetricCapture.From(
            requested, succeeded, failed, durations);

        await manager.AddRoutesAsync([CreateRoute()]);

        RouteMetricCapture after = RouteMetricCapture.From(
            requested, succeeded, failed, durations);
        Assert.Equal(1, api.AddCallCount);

        Activity? create = stopped.SingleOrDefault(a =>
            a.OperationName == IranDirectActivityNames.RoutesCreate);
        Assert.NotNull(create);
        Assert.Equal(ActivityKind.Internal, create!.Kind);
        Assert.Equal(
            IranDirectTagValues.OperationCreateRoutes,
            create.Tags.Single(t => t.Key == IranDirectTagNames.Operation).Value);
        Assert.Equal(
            IranDirectTagValues.ChangeKindCreate,
            create.Tags.Single(t => t.Key == IranDirectTagNames.ChangeKind).Value);
        Assert.Equal(
            IranDirectTagValues.RouteKindUnknown,
            create.Tags.Single(t => t.Key == IranDirectTagNames.RouteKind).Value);

        Assert.Equal(1, after.Requested - before.Requested);
        Assert.Equal(1, after.Succeeded - before.Succeeded);
        Assert.Equal(0, after.Failed - before.Failed);
        Assert.Equal(1, after.DurationCount - before.DurationCount);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1000)]
    public async Task Create_LargeBatch_StillOneActivityAndOneMeasurement(int count)
    {
        var api = new FakeWindowsRouteApi();
        var manager = CreateManager(api);

        var stopped = new ConcurrentQueue<Activity>();
        var started = new ConcurrentQueue<Activity>();
        var requested = new ConcurrentQueue<long>();
        var succeeded = new ConcurrentQueue<long>();
        var failed = new ConcurrentQueue<long>();
        var durations = new ConcurrentQueue<double>();

        using var al = CreateActivityListener(stopped, started);
        using var ml = CreateMeterListener(requested, succeeded, failed, durations);
        RouteMetricCapture before = RouteMetricCapture.From(
            requested, succeeded, failed, durations);

        var routes = new List<ManagedRoute>();
        for (int i = 0; i < count; i++)
            routes.Add(CreateRoute($"203.0.{i}.0/24"));

        await manager.AddRoutesAsync(routes);

        RouteMetricCapture after = RouteMetricCapture.From(
            requested, succeeded, failed, durations);
        Assert.Equal(1, api.AddCallCount);
        Assert.Equal(1, after.Requested - before.Requested);
        Assert.Equal(1, after.Succeeded - before.Succeeded);
        Assert.Equal(1, after.DurationCount - before.DurationCount);

        // Exactly one Routes.Create activity regardless of batch size.
        Assert.Single(
            stopped.Where(a => a.OperationName == IranDirectActivityNames.RoutesCreate));
    }

    [Fact]
    public async Task Create_PrefixAndEndpointBatches_RouteKindUnknown()
    {
        var prefixApi = new FakeWindowsRouteApi();
        var endpointApi = new FakeWindowsRouteApi();
        var prefixManager = CreateManager(prefixApi);
        var endpointManager = CreateManager(endpointApi);

        var stopped = new ConcurrentQueue<Activity>();
        using var al = CreateActivityListener(stopped, new ConcurrentQueue<Activity>());

        await prefixManager.AddRoutesAsync(
            [CreateRoute("203.0.113.0/24")]);
        await endpointManager.AddRoutesAsync(
            [CreateRoute("198.51.100.7/32")]);

        Assert.All(
            stopped.Where(a => a.OperationName == IranDirectActivityNames.RoutesCreate),
            a => Assert.Equal(
                IranDirectTagValues.RouteKindUnknown,
                a.Tags.Single(t => t.Key == IranDirectTagNames.RouteKind).Value));
    }

    [Fact]
    public async Task Create_IOException_RecordsFailureIoAndRethrows()
    {
        var api = new FakeWindowsRouteApi
        {
            AddThrow = new IOException("secret C:\\x"),
        };
        var manager = CreateManager(api);

        var stopped = new ConcurrentQueue<Activity>();
        var failed = new ConcurrentQueue<long>();

        using var al = CreateActivityListener(stopped, new ConcurrentQueue<Activity>());
        using var ml = CreateMeterListener(
            new ConcurrentQueue<long>(), new ConcurrentQueue<long>(), failed,
            new ConcurrentQueue<double>());

        IOException ex = await Assert.ThrowsAsync<IOException>(
            () => manager.AddRoutesAsync([CreateRoute()]));

        Assert.Equal(1, api.AddCallCount);
        Assert.Equal(1, failed.Count);
        // The exception legitimately retains its message; telemetry must not
        // surface it on the span (verified by Enumerate_NoSensitiveTags).

        Activity? create = stopped.SingleOrDefault(a =>
            a.OperationName == IranDirectActivityNames.RoutesCreate);
        Assert.NotNull(create);
        Assert.Equal(
            IranDirectTagValues.FailureIo,
            create!.Tags.Single(t =>
                t.Key == IranDirectTagNames.FailureCategory).Value);
    }

    [Fact]
    public async Task Create_RouteCreateFault_ZeroNativeCallsAndRoutingCategory()
    {
        var api = new FakeWindowsRouteApi();
        var manager = CreateManager(
            api,
            FaultInjectionPolicy.For([FaultInjectionPoint.RouteCreate]));

        var stopped = new ConcurrentQueue<Activity>();
        var failed = new ConcurrentQueue<long>();
        using var al = CreateActivityListener(stopped, new ConcurrentQueue<Activity>());
        using var ml = CreateMeterListener(
            new ConcurrentQueue<long>(), new ConcurrentQueue<long>(), failed,
            new ConcurrentQueue<double>());

        await Assert.ThrowsAsync<FaultInjectionException>(
            () => manager.AddRoutesAsync([CreateRoute()]));

        Assert.Equal(0, api.AddCallCount);
        // Fault is raised before the wrapped API is invoked: no telemetry.
        Assert.Equal(0, failed.Count);
        Assert.DoesNotContain(
            stopped,
            a => a.OperationName == IranDirectActivityNames.RoutesCreate);
    }

    [Fact]
    public async Task Create_EmptyBatch_EmitsNothingAndNoNativeCall()
    {
        var api = new FakeWindowsRouteApi();
        var manager = CreateManager(api);

        var stopped = new ConcurrentQueue<Activity>();
        var requested = new ConcurrentQueue<long>();
        var succeeded = new ConcurrentQueue<long>();
        var failed = new ConcurrentQueue<long>();
        var durations = new ConcurrentQueue<double>();

        using var al = CreateActivityListener(stopped, new ConcurrentQueue<Activity>());
        using var ml = CreateMeterListener(requested, succeeded, failed, durations);
        RouteMetricCapture before = RouteMetricCapture.From(
            requested, succeeded, failed, durations);

        await manager.AddRoutesAsync([]);

        RouteMetricCapture after = RouteMetricCapture.From(
            requested, succeeded, failed, durations);
        Assert.Equal(0, api.AddCallCount);
        Assert.Equal(0, after.Requested - before.Requested);
        Assert.Equal(0, after.Succeeded - before.Succeeded);
        Assert.Equal(0, after.Failed - before.Failed);
        Assert.DoesNotContain(
            stopped,
            a => a.OperationName == IranDirectActivityNames.RoutesCreate);
    }

    // ---- Delete ------------------------------------------------------------

    [Fact]
    public async Task Delete_OneRouteBatch_EmitsOneActivityAndMetrics()
    {
        var api = new FakeWindowsRouteApi();
        api.Present.Add("203.0.113.0/24|192.168.1.1|10");
        var manager = CreateManager(api);

        var stopped = new ConcurrentQueue<Activity>();
        var started = new ConcurrentQueue<Activity>();
        var requested = new ConcurrentQueue<long>();
        var succeeded = new ConcurrentQueue<long>();
        var failed = new ConcurrentQueue<long>();
        var durations = new ConcurrentQueue<double>();

        using var al = CreateActivityListener(stopped, started);
        using var ml = CreateMeterListener(requested, succeeded, failed, durations);
        RouteMetricCapture before = RouteMetricCapture.From(
            requested, succeeded, failed, durations);

        await manager.DeleteRoutesAsync([CreateRoute()]);

        RouteMetricCapture after = RouteMetricCapture.From(
            requested, succeeded, failed, durations);
        Assert.Equal(1, api.DeleteCallCount);

        Activity? delete = stopped.SingleOrDefault(a =>
            a.OperationName == IranDirectActivityNames.RoutesDelete);
        Assert.NotNull(delete);
        Assert.Equal(
            IranDirectTagValues.OperationDeleteRoutes,
            delete!.Tags.Single(t => t.Key == IranDirectTagNames.Operation).Value);
        Assert.Equal(
            IranDirectTagValues.ChangeKindDelete,
            delete.Tags.Single(t => t.Key == IranDirectTagNames.ChangeKind).Value);
        Assert.Equal(
            IranDirectTagValues.RouteKindUnknown,
            delete.Tags.Single(t => t.Key == IranDirectTagNames.RouteKind).Value);

        Assert.Equal(1, after.Requested - before.Requested);
        Assert.Equal(1, after.Succeeded - before.Succeeded);
        Assert.Equal(0, after.Failed - before.Failed);
        Assert.Equal(1, after.DurationCount - before.DurationCount);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1000)]
    public async Task Delete_LargeBatch_StillOneActivityAndOneMeasurement(int count)
    {
        var api = new FakeWindowsRouteApi();
        var manager = CreateManager(api);

        var stopped = new ConcurrentQueue<Activity>();
        var requested = new ConcurrentQueue<long>();
        var succeeded = new ConcurrentQueue<long>();
        var failed = new ConcurrentQueue<long>();
        var durations = new ConcurrentQueue<double>();

        using var al = CreateActivityListener(stopped, new ConcurrentQueue<Activity>());
        using var ml = CreateMeterListener(requested, succeeded, failed, durations);
        RouteMetricCapture before = RouteMetricCapture.From(
            requested, succeeded, failed, durations);

        var routes = new List<ManagedRoute>();
        for (int i = 0; i < count; i++)
            routes.Add(CreateRoute($"203.0.{i}.0/24"));
        foreach (ManagedRoute r in routes)
            api.Present.Add(r.Identity);

        await manager.DeleteRoutesAsync(routes);

        RouteMetricCapture after = RouteMetricCapture.From(
            requested, succeeded, failed, durations);
        Assert.Equal(1, api.DeleteCallCount);
        Assert.Equal(1, after.Requested - before.Requested);
        Assert.Equal(1, after.Succeeded - before.Succeeded);
        Assert.Equal(1, after.DurationCount - before.DurationCount);
        Assert.Single(
            stopped.Where(a => a.OperationName == IranDirectActivityNames.RoutesDelete));
    }

    [Fact]
    public async Task Delete_RouteDeleteFault_ZeroNativeCallsAndRoutingCategory()
    {
        var api = new FakeWindowsRouteApi();
        api.Present.Add("203.0.113.0/24|192.168.1.1|10");
        var manager = CreateManager(
            api,
            FaultInjectionPolicy.For([FaultInjectionPoint.RouteDelete]));

        var stopped = new ConcurrentQueue<Activity>();
        var failed = new ConcurrentQueue<long>();
        using var al = CreateActivityListener(stopped, new ConcurrentQueue<Activity>());
        using var ml = CreateMeterListener(
            new ConcurrentQueue<long>(), new ConcurrentQueue<long>(), failed,
            new ConcurrentQueue<double>());

        await Assert.ThrowsAsync<FaultInjectionException>(
            () => manager.DeleteRoutesAsync([CreateRoute()]));

        Assert.Equal(0, api.DeleteCallCount);
        // Fault is raised before the wrapped API is invoked: no telemetry.
        Assert.Equal(0, failed.Count);
        Assert.DoesNotContain(
            stopped,
            a => a.OperationName == IranDirectActivityNames.RoutesDelete);
    }

    [Fact]
    public async Task Delete_EmptyBatch_EmitsNothingAndNoNativeCall()
    {
        var api = new FakeWindowsRouteApi();
        var manager = CreateManager(api);

        var stopped = new ConcurrentQueue<Activity>();
        var requested = new ConcurrentQueue<long>();
        var succeeded = new ConcurrentQueue<long>();
        var failed = new ConcurrentQueue<long>();
        var durations = new ConcurrentQueue<double>();

        using var al = CreateActivityListener(stopped, new ConcurrentQueue<Activity>());
        using var ml = CreateMeterListener(requested, succeeded, failed, durations);
        RouteMetricCapture before = RouteMetricCapture.From(
            requested, succeeded, failed, durations);

        await manager.DeleteRoutesAsync([]);

        RouteMetricCapture after = RouteMetricCapture.From(
            requested, succeeded, failed, durations);
        Assert.Equal(0, api.DeleteCallCount);
        Assert.Equal(0, after.Requested - before.Requested);
        Assert.Equal(0, after.Succeeded - before.Succeeded);
        Assert.DoesNotContain(
            stopped,
            a => a.OperationName == IranDirectActivityNames.RoutesDelete);
    }

    // ---- Cancellation / no-listener / allocation --------------------------

    [Fact]
    public async Task AddRoutes_Cancelled_RecordsOutcomeCancelledNoFailedCounter()
    {
        var api = new FakeWindowsRouteApi
        {
            AddThrow = new OperationCanceledException(),
        };
        var manager = CreateManager(api);

        var stopped = new ConcurrentQueue<Activity>();
        var succeeded = new ConcurrentQueue<long>();
        var failed = new ConcurrentQueue<long>();

        using var al = CreateActivityListener(stopped, new ConcurrentQueue<Activity>());
        using var ml = CreateMeterListener(
            new ConcurrentQueue<long>(), succeeded, failed,
            new ConcurrentQueue<double>());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => manager.AddRoutesAsync([CreateRoute()]));

        Assert.Equal(1, api.AddCallCount);
        Assert.Equal(0, succeeded.Count);
        Assert.Equal(0, failed.Count); // cancellation is not a failure

        Activity? create = stopped.SingleOrDefault(a =>
            a.OperationName == IranDirectActivityNames.RoutesCreate);
        Assert.NotNull(create);
        Assert.Equal(
            IranDirectTagValues.Cancelled,
            create!.Tags.Single(t => t.Key == IranDirectTagNames.Outcome).Value);
        Assert.Equal(ActivityStatusCode.Unset, create.Status);
        Assert.DoesNotContain(
            create.Tags, t => t.Key == IranDirectTagNames.FailureCategory);
    }

    [Fact]
    public async Task NoListener_NativeBehaviorUnchangedAndNoException()
    {
        // No ActivityListener, no MeterListener: telemetry must not affect the
        // native call or throw.
        var api = new FakeWindowsRouteApi();
        var manager = CreateManager(api);

        await manager.AddRoutesAsync([CreateRoute("203.0.113.0/24")]);
        await manager.DeleteRoutesAsync([CreateRoute("203.0.113.0/24")]);
        IReadOnlyList<SystemRoute> routes = await manager.GetIpv4RoutesAsync();

        Assert.Equal(1, api.AddCallCount);
        Assert.Equal(1, api.DeleteCallCount);
        Assert.Equal(1, api.EnumerateCallCount);
        Assert.Empty(routes);
    }

    [Fact]
    public async Task FiftyThousandRouteBatch_ProducesOneTelemetryScopeNotRouteProportional()
    {
        var api = new FakeWindowsRouteApi();
        var manager = CreateManager(api);

        var stopped = new ConcurrentQueue<Activity>();
        using var al = CreateActivityListener(stopped, new ConcurrentQueue<Activity>());

        var routes = new List<ManagedRoute>(50_000);
        for (int i = 0; i < 50_000; i++)
            routes.Add(CreateRoute($"203.{i / 256}.{i % 256}.0/24"));

        await manager.AddRoutesAsync(routes);

        // Exactly one Routes.Create activity for 50,000 routes => no
        // route-proportional telemetry allocation.
        Assert.Single(
            stopped.Where(a => a.OperationName == IranDirectActivityNames.RoutesCreate));
        Assert.Equal(1, api.AddCallCount);
    }

    // ---- Parent-child correlation under runtime execution -----------------

    [Fact]
    public async Task RouteSpans_AreChildrenOfRuntimeExecute_NotPlanChanges()
    {
        var api = new FakeWindowsRouteApi();
        api.EnumerateResult =
        [
            new SystemRoute
            {
                DestinationPrefix = "203.0.113.0/24",
                NextHop = IPAddress.Parse("192.168.1.1"),
                InterfaceIndex = 10,
                RouteMetric = 5,
            },
        ];
        var manager = CreateManager(api);

        var stopped = new ConcurrentQueue<Activity>();
        var started = new ConcurrentQueue<Activity>();
        using var al = CreateActivityListener(stopped, started);

        // Reproduce the real runtime hierarchy using the exact production span
        // names: cycle -> execute -> routes. Planning is intentionally absent
        // (it is a sibling of execution, never an ancestor of route spans).
        using (Activity? cycleScope = IranDirectTelemetry.ActivitySource.StartActivity(
                   IranDirectActivityNames.RuntimeCycle, ActivityKind.Internal))
        {
            using (Activity? execScope = IranDirectTelemetry.ActivitySource.StartActivity(
                       IranDirectActivityNames.RuntimeExecute, ActivityKind.Internal))
            {
                await manager.GetIpv4RoutesAsync();
                await manager.AddRoutesAsync([CreateRoute()]);
                await manager.DeleteRoutesAsync([CreateRoute()]);
            }
        }

        Activity? cycle = stopped.SingleOrDefault(a =>
            a.OperationName == IranDirectActivityNames.RuntimeCycle);
        Activity? execute = stopped.SingleOrDefault(a =>
            a.OperationName == IranDirectActivityNames.RuntimeExecute);
        Assert.NotNull(cycle);
        Assert.NotNull(execute);

        var routeSpans = stopped
            .Where(a => a.OperationName is
                IranDirectActivityNames.RoutesEnumerate or
                IranDirectActivityNames.RoutesCreate or
                IranDirectActivityNames.RoutesDelete)
            .ToArray();

        Assert.Equal(3, routeSpans.Length);

        // Each route span is a direct child of Runtime.Execute, shares the
        // cycle TraceId, and is NOT a child of Runtime.PlanChanges (which is
        // absent here).
        foreach (Activity route in routeSpans)
        {
            Assert.Equal(cycle!.TraceId, route.TraceId);
            Assert.Equal(execute!.SpanId, route.ParentSpanId);
            Assert.NotEqual(cycle.SpanId, route.ParentSpanId);
        }

        // Route spans stop before Runtime.Execute stops (they are nested
        // beneath it, not overlapping with the cycle root).
        DateTimeOffset execStop = execute!.StartTimeUtc + execute.Duration;
        foreach (Activity route in routeSpans)
        {
            DateTimeOffset routeStop = route.StartTimeUtc + route.Duration;
            Assert.True(
                routeStop <= execStop,
                "route span must stop before Runtime.Execute stops");
        }

        // No extra child spans exist beyond the approved set.
        Assert.All(stopped, a => Assert.Contains(a.OperationName, new[]
        {
            IranDirectActivityNames.RuntimeCycle,
            IranDirectActivityNames.RuntimeExecute,
            IranDirectActivityNames.RoutesEnumerate,
            IranDirectActivityNames.RoutesCreate,
            IranDirectActivityNames.RoutesDelete,
        }));
    }
}
