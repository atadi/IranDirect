using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;
using PathVeer.Core.Ipc;
using PathVeer.Core.Observability.Telemetry;
using PathVeer.Core.Testing.FaultInjection;
using Xunit;

namespace PathVeer.Core.Tests.Observability.Telemetry;

/// <summary>
/// Behavior + allocation-free verification for the IPC request/response
/// telemetry produced by <see cref="PathVeerServiceClient.SendAsync"/>.
/// All assertions pin the established contracts from the committed
/// <see cref="TelemetryFailureCategoryMapper"/> and
/// <see cref="TelemetryOutcomeMapper"/> rather than inventing categories.
/// </summary>
[Collection("RuntimeCycleTelemetry")]
public sealed class IpcRequestTelemetryTests
{
    private static ActivityListener CreateActivityListener(
        ConcurrentQueue<Activity> started,
        ConcurrentQueue<Activity> stopped)
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = s =>
                s.Name == PathVeerTelemetry.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = a => started.Enqueue(a),
            ActivityStopped = a => stopped.Enqueue(a),
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    private static MeterListener CreateMeterListener(
        ConcurrentQueue<long> requests,
        ConcurrentQueue<double> durations)
    {
        var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == PathVeerTelemetry.SourceName)
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>(
            (instrument, value, tags, state) =>
            {
                if (instrument.Name == PathVeerMetricNames.IpcRequests)
                {
                    requests.Enqueue(value);
                }
            });
        listener.SetMeasurementEventCallback<double>(
            (instrument, value, tags, state) =>
            {
                if (instrument.Name == PathVeerMetricNames.IpcRequestDuration)
                {
                    durations.Enqueue(value);
                }
            });
        listener.Start();
        return listener;
    }

    [Fact]
    public void SendAsync_Success_EmitsRootWithConnectSendReceiveChildren()
    {
        var started = new ConcurrentQueue<Activity>();
        var stopped = new ConcurrentQueue<Activity>();
        var requests = new ConcurrentQueue<long>();
        var durations = new ConcurrentQueue<double>();

        using var al = CreateActivityListener(started, stopped);
        using var ml = CreateMeterListener(requests, durations);

        FakeNamedPipeClientFactory factory = CreateFactory(SuccessResponseLine());
        PathVeerServiceClient client = CreateClient(factory);

        ServiceResponse response = client.SendAsync(
            PathVeerCommand.Status).GetAwaiter().GetResult();

        Assert.True(response.Success);

        Activity root = Assert.Single(
            started.Where(a =>
                a.OperationName == PathVeerActivityNames.IpcRequest));
        Assert.Equal(
            PathVeerTagValues.OperationIpcRequest,
            root.Tags.Single(t => t.Key == PathVeerTagNames.Operation).Value);
        Assert.Equal(
            TelemetryOutcomeMapper.Map(PathVeerCommand.Status),
            root.Tags.Single(t => t.Key == PathVeerTagNames.IpcCommand).Value);

        Activity connect = Assert.Single(
            stopped.Where(a =>
                a.OperationName == PathVeerActivityNames.IpcConnect));
        Activity send = Assert.Single(
            stopped.Where(a =>
                a.OperationName == PathVeerActivityNames.IpcSend));
        Activity receive = Assert.Single(
            stopped.Where(a =>
                a.OperationName == PathVeerActivityNames.IpcReceive));

        Assert.Equal(
            PathVeerTagValues.Success,
            connect.Tags.Single(t => t.Key == PathVeerTagNames.Outcome).Value);
        Assert.Equal(
            PathVeerTagValues.Success,
            send.Tags.Single(t => t.Key == PathVeerTagNames.Outcome).Value);
        Assert.Equal(
            PathVeerTagValues.Success,
            receive.Tags.Single(t => t.Key == PathVeerTagNames.Outcome).Value);

        // Children are parented to the root.
        Assert.Equal(root.SpanId, connect.ParentSpanId);
        Assert.Equal(root.SpanId, send.ParentSpanId);
        Assert.Equal(root.SpanId, receive.ParentSpanId);

        Assert.Equal(
            ActivityStatusCode.Ok, root.Status);
        Assert.Single(requests);
        Assert.Single(durations);
    }

    [Fact]
    public void SendAsync_FailedServiceResponse_TransportChildrenSucceedRootFails()
    {
        // The response was transported and parsed correctly; only the business
        // response failed. Connect/Send/Receive stay success, root is failure.
        var started = new ConcurrentQueue<Activity>();
        var stopped = new ConcurrentQueue<Activity>();
        var requests = new ConcurrentQueue<long>();
        var durations = new ConcurrentQueue<double>();

        using var al = CreateActivityListener(started, stopped);
        using var ml = CreateMeterListener(requests, durations);

        FakeNamedPipeClientFactory factory = CreateFactory(
            FailedResponseLine("INVALID_BUNDLE_PATH"));
        PathVeerServiceClient client = CreateClient(factory);

        ServiceResponse response = client.SendAsync(
            PathVeerCommand.SupportBundleExport).GetAwaiter().GetResult();

        Assert.False(response.Success);
        Assert.Equal("INVALID_BUNDLE_PATH", response.ErrorCode);

        Activity root = Assert.Single(
            started.Where(a =>
                a.OperationName == PathVeerActivityNames.IpcRequest));

        Assert.Equal(
            PathVeerTagValues.Failure,
            root.Tags.Single(t => t.Key == PathVeerTagNames.Outcome).Value);
        // A business-level failure with no telemetry-defined category carries
        // no failure_category tag.
        Assert.DoesNotContain(
            root.Tags,
            t => t.Key == PathVeerTagNames.FailureCategory);

        Assert.Equal(
            PathVeerTagValues.Success,
            Assert.Single(stopped.Where(a =>
                a.OperationName == PathVeerActivityNames.IpcConnect))
                .Tags.Single(t => t.Key == PathVeerTagNames.Outcome).Value);
        Assert.Equal(
            PathVeerTagValues.Success,
            Assert.Single(stopped.Where(a =>
                a.OperationName == PathVeerActivityNames.IpcSend))
                .Tags.Single(t => t.Key == PathVeerTagNames.Outcome).Value);
        Assert.Equal(
            PathVeerTagValues.Success,
            Assert.Single(stopped.Where(a =>
                a.OperationName == PathVeerActivityNames.IpcReceive))
                .Tags.Single(t => t.Key == PathVeerTagNames.Outcome).Value);

        AssertNoFreeFormTags(root, response.Message);
        Assert.Single(requests);
    }

    [Fact]
    public void SendAsync_SendFaultBeforeConnect_NoChildSpansRootFailsViaMapper()
    {
        // The fault is raised before any connection is attempted, so the
        // Connect/Send/Receive children must not exist. The root still starts
        // (a request attempt began) and fails; the category is whatever the
        // committed mapper returns for NamedPipeSend (asserted, not assumed).
        var started = new ConcurrentQueue<Activity>();
        var stopped = new ConcurrentQueue<Activity>();
        var requests = new ConcurrentQueue<long>();
        var durations = new ConcurrentQueue<double>();

        using var al = CreateActivityListener(started, stopped);
        using var ml = CreateMeterListener(requests, durations);

        FakeNamedPipeClientFactory factory = CreateFactory(SuccessResponseLine());
        PathVeerServiceClient client = CreateClient(
            factory,
            FaultInjectionPolicy.For(
                [FaultInjectionPoint.NamedPipeSend]));

        FaultInjectionException thrown = Assert.Throws<FaultInjectionException>(
            () => client.SendAsync(PathVeerCommand.Status)
                .GetAwaiter().GetResult());

        Assert.Equal(FaultInjectionPoint.NamedPipeSend, thrown.Point);

        Activity root = Assert.Single(
            started.Where(a =>
                a.OperationName == PathVeerActivityNames.IpcRequest));

        Assert.Equal(
            PathVeerTagValues.Failure,
            root.Tags.Single(t => t.Key == PathVeerTagNames.Outcome).Value);

        // Pin the established contract: do NOT hardcode io here.
        string expectedCategory =
            TelemetryFailureCategoryMapper.ToCategoryString(
                TelemetryFailureCategoryMapper.Map(
                    new FaultInjectionException(
                        FaultInjectionPoint.NamedPipeSend)));
        Assert.Equal(
            expectedCategory,
            root.Tags.Single(t => t.Key == PathVeerTagNames.FailureCategory)
                .Value);

        // No transport children were created.
        Assert.DoesNotContain(
            started,
            a => a.OperationName == PathVeerActivityNames.IpcConnect);
        Assert.DoesNotContain(
            started,
            a => a.OperationName == PathVeerActivityNames.IpcSend);
        Assert.DoesNotContain(
            started,
            a => a.OperationName == PathVeerActivityNames.IpcReceive);
        Assert.Equal(0, factory.ConnectCallCount);

        Assert.Single(requests);
        Assert.Single(durations);
    }

    [Fact]
    public void SendAsync_ConnectTimeout_ChildAndRootTimeout()
    {
        var started = new ConcurrentQueue<Activity>();
        var stopped = new ConcurrentQueue<Activity>();
        var requests = new ConcurrentQueue<long>();
        var durations = new ConcurrentQueue<double>();

        using var al = CreateActivityListener(started, stopped);
        using var ml = CreateMeterListener(requests, durations);

        FakeNamedPipeClientFactory factory = new(
            cancellationToken => throw new OperationCanceledException(
                cancellationToken));
        PathVeerServiceClient client = CreateClient(factory);

        TimeoutException exception = Assert.Throws<TimeoutException>(
            () => client.SendAsync(PathVeerCommand.Status)
                .GetAwaiter().GetResult());

        Assert.Equal(
            "IranDirect Service is unavailable or did not " +
            "accept the connection within 5 seconds.",
            exception.Message);
        Assert.Equal(1, factory.ConnectCallCount);

        Activity root = Assert.Single(
            started.Where(a =>
                a.OperationName == PathVeerActivityNames.IpcRequest));
        Activity connect = Assert.Single(
            started.Where(a =>
                a.OperationName == PathVeerActivityNames.IpcConnect));

        // Connect timeout: outcome=timeout. The committed TelemetryOutcomeMapper
        // maps TimeoutException to (Timeout, category: null), so no
        // failure_category tag is attached (timeout is not a categorized fault).
        Assert.Equal(
            PathVeerTagValues.Timeout,
            root.Tags.Single(t => t.Key == PathVeerTagNames.Outcome).Value);
        Assert.DoesNotContain(
            root.Tags,
            t => t.Key == PathVeerTagNames.FailureCategory);

        Assert.Equal(
            PathVeerTagValues.Timeout,
            connect.Tags.Single(t => t.Key == PathVeerTagNames.Outcome).Value);
        Assert.DoesNotContain(
            connect.Tags,
            t => t.Key == PathVeerTagNames.FailureCategory);

        Assert.Single(requests);
        Assert.Single(durations);
    }

    [Fact]
    public void SendAsync_PreCanceled_ConnectAttemptedRootCancelledNoCategory()
    {
        // Pre-cancelled call: the connect is still attempted once (so the
        // connect child exists and is cancelled), the root is Cancelled with
        // status Unset and no failure_category. Serialization happens before
        // cancellation is observed, so we do NOT assert zero serialization.
        var started = new ConcurrentQueue<Activity>();
        var stopped = new ConcurrentQueue<Activity>();
        var requests = new ConcurrentQueue<long>();
        var durations = new ConcurrentQueue<double>();

        using var al = CreateActivityListener(started, stopped);
        using var ml = CreateMeterListener(requests, durations);

        using CancellationTokenSource callerCts = new();
        callerCts.Cancel();

        FakeNamedPipeClientFactory factory = new(
            cancellationToken =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult<INamedPipeClientConnection>(
                    new FakeNamedPipeClientConnection(SuccessResponseLine()));
            });
        PathVeerServiceClient client = CreateClient(factory);

        Assert.Throws<OperationCanceledException>(
            () => client.SendAsync(
                PathVeerCommand.Status,
                cancellationToken: callerCts.Token).GetAwaiter().GetResult());

        Assert.Equal(1, factory.ConnectCallCount);

        Activity root = Assert.Single(
            started.Where(a =>
                a.OperationName == PathVeerActivityNames.IpcRequest));
        Assert.Equal(
            PathVeerTagValues.Cancelled,
            root.Tags.Single(t => t.Key == PathVeerTagNames.Outcome).Value);
        Assert.Equal(ActivityStatusCode.Unset, root.Status);
        Assert.DoesNotContain(
            root.Tags,
            t => t.Key == PathVeerTagNames.FailureCategory);

        Activity connect = Assert.Single(
            stopped.Where(a =>
                a.OperationName == PathVeerActivityNames.IpcConnect));
        Assert.Equal(
            PathVeerTagValues.Cancelled,
            connect.Tags.Single(t => t.Key == PathVeerTagNames.Outcome).Value);

        Assert.DoesNotContain(
            started,
            a => a.OperationName == PathVeerActivityNames.IpcSend);
        Assert.DoesNotContain(
            started,
            a => a.OperationName == PathVeerActivityNames.IpcReceive);
    }

    [Fact]
    public void SendAsync_WriteFailure_ConnectsSucceedsSendFailsNoReceive()
    {
        var started = new ConcurrentQueue<Activity>();
        var stopped = new ConcurrentQueue<Activity>();
        var requests = new ConcurrentQueue<long>();
        var durations = new ConcurrentQueue<double>();

        using var al = CreateActivityListener(started, stopped);
        using var ml = CreateMeterListener(requests, durations);

        FakeNamedPipeClientFactory factory = new(
            cancellationToken =>
                Task.FromResult<INamedPipeClientConnection>(
                    new FakeNamedPipeClientConnection(throwOnWrite: true)));
        PathVeerServiceClient client = CreateClient(factory);

        Assert.Throws<IOException>(
            () => client.SendAsync(PathVeerCommand.Status)
                .GetAwaiter().GetResult());

        Activity root = Assert.Single(
            started.Where(a =>
                a.OperationName == PathVeerActivityNames.IpcRequest));
        Assert.Equal(
            PathVeerTagValues.Failure,
            root.Tags.Single(t => t.Key == PathVeerTagNames.Outcome).Value);
        Assert.Equal(
            PathVeerTagValues.FailureIo,
            root.Tags.Single(t => t.Key == PathVeerTagNames.FailureCategory)
                .Value);

        Assert.Equal(
            PathVeerTagValues.Success,
            Assert.Single(stopped.Where(a =>
                a.OperationName == PathVeerActivityNames.IpcConnect))
                .Tags.Single(t => t.Key == PathVeerTagNames.Outcome).Value);
        Activity send = Assert.Single(
            stopped.Where(a =>
                a.OperationName == PathVeerActivityNames.IpcSend));
        Assert.Equal(
            PathVeerTagValues.Failure,
            send.Tags.Single(t => t.Key == PathVeerTagNames.Outcome).Value);
        Assert.Equal(
            PathVeerTagValues.FailureIo,
            send.Tags.Single(t => t.Key == PathVeerTagNames.FailureCategory)
                .Value);

        Assert.DoesNotContain(
            started,
            a => a.OperationName == PathVeerActivityNames.IpcReceive);
    }

    [Fact]
    public void SendAsync_ReadFailure_ConnectAndSendSucceedReceiveFails()
    {
        var started = new ConcurrentQueue<Activity>();
        var stopped = new ConcurrentQueue<Activity>();
        var requests = new ConcurrentQueue<long>();
        var durations = new ConcurrentQueue<double>();

        using var al = CreateActivityListener(started, stopped);
        using var ml = CreateMeterListener(requests, durations);

        FakeNamedPipeClientFactory factory = new(
            cancellationToken =>
                Task.FromResult<INamedPipeClientConnection>(
                    new FakeNamedPipeClientConnection(throwOnRead: true)));
        PathVeerServiceClient client = CreateClient(factory);

        Assert.Throws<IOException>(
            () => client.SendAsync(PathVeerCommand.Status)
                .GetAwaiter().GetResult());

        Assert.Equal(
            PathVeerTagValues.Success,
            Assert.Single(stopped.Where(a =>
                a.OperationName == PathVeerActivityNames.IpcConnect))
                .Tags.Single(t => t.Key == PathVeerTagNames.Outcome).Value);
        Assert.Equal(
            PathVeerTagValues.Success,
            Assert.Single(stopped.Where(a =>
                a.OperationName == PathVeerActivityNames.IpcSend))
                .Tags.Single(t => t.Key == PathVeerTagNames.Outcome).Value);

        Activity receive = Assert.Single(
            stopped.Where(a =>
                a.OperationName == PathVeerActivityNames.IpcReceive));
        Assert.Equal(
            PathVeerTagValues.Failure,
            receive.Tags.Single(t => t.Key == PathVeerTagNames.Outcome).Value);
        Assert.Equal(
            PathVeerTagValues.FailureIo,
            receive.Tags.Single(t => t.Key == PathVeerTagNames.FailureCategory)
                .Value);
    }

    [Fact]
    public void SendAsync_NullJsonResponse_MapsInvalidResponse()
    {
        var started = new ConcurrentQueue<Activity>();
        var stopped = new ConcurrentQueue<Activity>();

        using var al = CreateActivityListener(started, stopped);

        FakeNamedPipeClientFactory factory = CreateFactory(
            responseLine: "null");
        PathVeerServiceClient client = CreateClient(factory);

        ServiceResponse response = client.SendAsync(
            PathVeerCommand.Status).GetAwaiter().GetResult();

        Assert.False(response.Success);
        Assert.Equal("INVALID_RESPONSE", response.ErrorCode);

        Activity root = Assert.Single(
            started.Where(a =>
                a.OperationName == PathVeerActivityNames.IpcRequest));
        Assert.Equal(
            PathVeerTagValues.Failure,
            root.Tags.Single(t => t.Key == PathVeerTagNames.Outcome).Value);
        Assert.Equal(
            PathVeerTagValues.FailureInvalidResponse,
            root.Tags.Single(t => t.Key == PathVeerTagNames.FailureCategory)
                .Value);

        // Transport children succeeded; only the response shape failed.
        Assert.Equal(
            PathVeerTagValues.Success,
            Assert.Single(stopped.Where(a =>
                a.OperationName == PathVeerActivityNames.IpcReceive))
                .Tags.Single(t => t.Key == PathVeerTagNames.Outcome).Value);
    }

    [Fact]
    public void SendAsync_MalformedJson_MapsSerializationViaMapper()
    {
        var started = new ConcurrentQueue<Activity>();
        var stopped = new ConcurrentQueue<Activity>();

        using var al = CreateActivityListener(started, stopped);

        FakeNamedPipeClientFactory factory = new();
        PathVeerServiceClient client = CreateClient(factory);

        Assert.Throws<JsonException>(
            () => client.SendAsync(PathVeerCommand.Status)
                .GetAwaiter().GetResult());

        Activity root = Assert.Single(
            started.Where(a =>
                a.OperationName == PathVeerActivityNames.IpcRequest));
        Assert.Equal(
            PathVeerTagValues.Failure,
            root.Tags.Single(t => t.Key == PathVeerTagNames.Outcome).Value);

        string expected = TelemetryFailureCategoryMapper.ToCategoryString(
            TelemetryFailureCategoryMapper.Map(new JsonException()));
        Assert.Equal(
            expected,
            root.Tags.Single(t => t.Key == PathVeerTagNames.FailureCategory)
                .Value);
    }

    [Fact]
    public void SendAsync_NoListener_BehaviorUnchanged()
    {
        // Without any ActivityListener, the telemetry is a no-op and behavior
        // is identical to the non-instrumented path.
        FakeNamedPipeClientFactory factory = CreateFactory(SuccessResponseLine());
        PathVeerServiceClient client = CreateClient(factory);

        ServiceResponse response = client.SendAsync(
            PathVeerCommand.Status).GetAwaiter().GetResult();

        Assert.True(response.Success);
        Assert.Equal(1, factory.ConnectCallCount);
    }

    private static void AssertNoFreeFormTags(
        Activity activity,
        string responseMessage)
    {
        foreach (var tag in activity.Tags)
        {
            Assert.DoesNotContain(
                tag.Key,
                PathVeerTagNames.Prohibited,
                StringComparer.Ordinal);
            Assert.False(
                tag.Key.Equals("reason", StringComparison.Ordinal) ||
                tag.Key.Equals("message", StringComparison.Ordinal));
            if (tag.Value is string s)
            {
                Assert.DoesNotContain(responseMessage, s);
            }
        }
    }

    private static PathVeerServiceClient CreateClient(
        FakeNamedPipeClientFactory factory,
        IFaultInjectionPolicy? faultPolicy = null) =>
        new(factory, faultPolicy);

    private static FakeNamedPipeClientFactory CreateFactory(
        string responseLine) =>
        new(
            cancellationToken =>
                Task.FromResult<INamedPipeClientConnection>(
                    new FakeNamedPipeClientConnection(responseLine)));

    private static string SuccessResponseLine() =>
        JsonSerializer.Serialize(
            new ServiceResponse { Success = true, Message = "ok." },
            PathVeerJson.Options);

    private static string FailedResponseLine(string errorCode) =>
        JsonSerializer.Serialize(
            new ServiceResponse
            {
                Success = false,
                ErrorCode = errorCode,
                Message = $"business failure {errorCode}"
            },
            PathVeerJson.Options);

    private sealed class FakeNamedPipeClientFactory :
        INamedPipeClientFactory
    {
        private readonly Func<
            CancellationToken,
            Task<INamedPipeClientConnection>> _connect;

        public FakeNamedPipeClientFactory(
            Func<
                CancellationToken,
                Task<INamedPipeClientConnection>>? connect = null)
        {
            _connect = connect ?? DefaultConnect;
        }

        public int ConnectCallCount { get; private set; }

        public FakeNamedPipeClientConnection? LastConnection { get; private set; }

        public async Task<INamedPipeClientConnection> ConnectAsync(
            CancellationToken cancellationToken)
        {
            ConnectCallCount++;
            INamedPipeClientConnection connection =
                await _connect(cancellationToken);
            LastConnection =
                connection as FakeNamedPipeClientConnection;
            return connection;
        }

        private static async Task<INamedPipeClientConnection> DefaultConnect(
            CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            return new FakeNamedPipeClientConnection();
        }
    }

    private sealed class FakeNamedPipeClientConnection :
        INamedPipeClientConnection
    {
        private readonly string? _responseLine;
        private readonly bool _throwOnWrite;
        private readonly bool _throwOnRead;
        private int _disposed;

        public FakeNamedPipeClientConnection(
            string? responseLine = null,
            bool throwOnWrite = false,
            bool throwOnRead = false)
        {
            _responseLine = responseLine;
            _throwOnWrite = throwOnWrite;
            _throwOnRead = throwOnRead;
        }

        public int WriteCallCount { get; private set; }

        public int ReadCallCount { get; private set; }

        public int DisposeCount { get; private set; }

        public bool IsDisposed => _disposed != 0;

        public Task WriteRequestLineAsync(
            string requestJson,
            CancellationToken cancellationToken)
        {
            WriteCallCount++;
            if (_throwOnWrite)
            {
                throw new IOException("write failed");
            }

            return Task.CompletedTask;
        }

        public ValueTask<string?> ReadResponseLineAsync(
            CancellationToken cancellationToken)
        {
            ReadCallCount++;
            if (_throwOnRead)
            {
                throw new IOException("read failed");
            }

            return new ValueTask<string?>(_responseLine);
        }

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                DisposeCount++;
            }

            return ValueTask.CompletedTask;
        }
    }
}
