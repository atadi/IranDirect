using System.IO;
using System.Text.Json;
using IranDirect.Core.Ipc;
using IranDirect.Core.Testing.FaultInjection;

namespace IranDirect.Core.Tests.Ipc;

public sealed class IranDirectServiceClientFaultInjectionTests
{
    [Fact]
    public async Task SendAsync_NamedPipeSendFault_ExposesCorrectPoint()
    {
        FakeNamedPipeClientFactory factory = CreateFactory(SuccessResponseLine());
        IranDirectServiceClient client = CreateClient(
            factory,
            FaultInjectionPolicy.For(
                [FaultInjectionPoint.NamedPipeSend]));

        FaultInjectionException exception =
            await Assert.ThrowsAsync<FaultInjectionException>(
                () => client.SendAsync(IranDirectCommand.Status));

        Assert.Equal(
            FaultInjectionPoint.NamedPipeSend,
            exception.Point);
    }

    [Fact]
    public async Task SendAsync_NamedPipeSendFault_ZeroConnectionAttempts()
    {
        FakeNamedPipeClientFactory factory = CreateFactory(SuccessResponseLine());
        IranDirectServiceClient client = CreateClient(
            factory,
            FaultInjectionPolicy.For(
                [FaultInjectionPoint.NamedPipeSend]));

        await Assert.ThrowsAsync<FaultInjectionException>(
            () => client.SendAsync(IranDirectCommand.Status));

        Assert.Equal(0, factory.ConnectCallCount);
        Assert.Null(factory.LastConnection);
    }

    [Fact]
    public async Task SendAsync_NamedPipeSendFault_ZeroWriteAttempts()
    {
        FakeNamedPipeClientFactory factory = CreateFactory(SuccessResponseLine());
        IranDirectServiceClient client = CreateClient(
            factory,
            FaultInjectionPolicy.For(
                [FaultInjectionPoint.NamedPipeSend]));

        await Assert.ThrowsAsync<FaultInjectionException>(
            () => client.SendAsync(IranDirectCommand.Status));

        Assert.Null(factory.LastConnection);
        Assert.Equal(0, factory.ConnectCallCount);
        Assert.Equal(0, factory.LastConnection?.WriteCallCount ?? 0);
    }

    [Fact]
    public async Task SendAsync_NamedPipeSendFault_DoesNotRetry()
    {
        FakeNamedPipeClientFactory factory = CreateFactory(SuccessResponseLine());
        IranDirectServiceClient client = CreateClient(
            factory,
            FaultInjectionPolicy.For(
                [FaultInjectionPoint.NamedPipeSend]));

        FaultInjectionException exception =
            await Assert.ThrowsAsync<FaultInjectionException>(
                () => client.SendAsync(IranDirectCommand.Status));

        Assert.Equal(
            FaultInjectionPoint.NamedPipeSend,
            exception.Point);
        Assert.Equal(0, factory.ConnectCallCount);
        Assert.Null(factory.LastConnection);
    }

    [Fact]
    public async Task SendAsync_NextUnfaultedSend_Succeeds()
    {
        FakeNamedPipeClientFactory factory = CreateFactory(SuccessResponseLine());
        IranDirectServiceClient client = CreateClient(factory);

        FaultInjectionException exception;
        using (FaultInjectionScope scope =
            FaultInjectionScope.Fail(
                FaultInjectionPoint.NamedPipeSend))
        {
            exception = await Assert.ThrowsAsync<FaultInjectionException>(
                () => client.SendAsync(IranDirectCommand.Status));
        }

        Assert.Equal(
            FaultInjectionPoint.NamedPipeSend,
            exception.Point);
        Assert.Equal(0, factory.ConnectCallCount);

        ServiceResponse response =
            await client.SendAsync(IranDirectCommand.Status);

        Assert.True(response.Success);
        Assert.Equal(1, factory.ConnectCallCount);
        Assert.Equal(1, factory.LastConnection!.WriteCallCount);
        Assert.Equal(1, factory.LastConnection.ReadCallCount);
    }

    [Fact]
    public async Task SendAsync_UnrelatedFaultPoint_HasNoEffect()
    {
        FakeNamedPipeClientFactory factory = CreateFactory(SuccessResponseLine());
        IranDirectServiceClient client = CreateClient(
            factory,
            FaultInjectionPolicy.For(
                [FaultInjectionPoint.HttpRequest]));

        ServiceResponse response =
            await client.SendAsync(IranDirectCommand.Status);

        Assert.True(response.Success);
        Assert.Equal(1, factory.ConnectCallCount);
        Assert.Equal(1, factory.LastConnection!.WriteCallCount);
    }

    [Fact]
    public async Task SendAsync_InjectedPolicy_WorksWithoutAmbientScope()
    {
        FakeNamedPipeClientFactory factory = CreateFactory(SuccessResponseLine());
        IranDirectServiceClient client = CreateClient(
            factory,
            FaultInjectionPolicy.For(
                [FaultInjectionPoint.NamedPipeSend]));

        FaultInjectionException exception =
            await Assert.ThrowsAsync<FaultInjectionException>(
                () => client.SendAsync(IranDirectCommand.Status));

        Assert.Equal(
            FaultInjectionPoint.NamedPipeSend,
            exception.Point);
        Assert.Equal(0, factory.ConnectCallCount);
    }

    [Fact]
    public async Task SendAsync_AmbientScope_OverridesInjectedPolicy_TriggersFault()
    {
        FakeNamedPipeClientFactory factory = CreateFactory(SuccessResponseLine());
        IranDirectServiceClient client = CreateClient(factory);

        using (FaultInjectionScope scope =
            FaultInjectionScope.Fail(
                FaultInjectionPoint.NamedPipeSend))
        {
            await Assert.ThrowsAsync<FaultInjectionException>(
                () => client.SendAsync(IranDirectCommand.Status));
        }

        Assert.Equal(0, factory.ConnectCallCount);
    }

    [Fact]
    public async Task SendAsync_AmbientScope_OverridesInjectedPolicy_SuppressesFault()
    {
        FakeNamedPipeClientFactory factory = CreateFactory(SuccessResponseLine());
        IranDirectServiceClient client = CreateClient(
            factory,
            FaultInjectionPolicy.For(
                [FaultInjectionPoint.NamedPipeSend]));

        using (FaultInjectionScope scope =
            FaultInjectionScope.Fail(
                FaultInjectionPoint.HttpRequest))
        {
            ServiceResponse response =
                await client.SendAsync(IranDirectCommand.Status);

            Assert.True(response.Success);
        }

        Assert.Equal(1, factory.ConnectCallCount);
        Assert.Equal(1, factory.LastConnection!.WriteCallCount);
    }

    [Fact]
    public async Task SendAsync_NestedScopes_InnerScopeTriggersCorrectPoint()
    {
        FakeNamedPipeClientFactory factory = CreateFactory(SuccessResponseLine());
        IranDirectServiceClient client = CreateClient(factory);

        using (FaultInjectionScope outer =
            FaultInjectionScope.Fail(
                FaultInjectionPoint.HttpRequest))
        {
            using (FaultInjectionScope inner =
                FaultInjectionScope.Fail(
                    FaultInjectionPoint.NamedPipeSend))
            {
                FaultInjectionException exception =
                    await Assert.ThrowsAsync<FaultInjectionException>(
                        () => client.SendAsync(IranDirectCommand.Status));

                Assert.Equal(
                    FaultInjectionPoint.NamedPipeSend,
                    exception.Point);
            }
        }

        Assert.Equal(0, factory.ConnectCallCount);
    }

    [Fact]
    public async Task SendAsync_NestedScopes_UnselectedInnerScope_NoFault()
    {
        FakeNamedPipeClientFactory factory = CreateFactory(SuccessResponseLine());
        IranDirectServiceClient client = CreateClient(factory);

        using (FaultInjectionScope outer =
            FaultInjectionScope.Fail(
                FaultInjectionPoint.NamedPipeSend))
        {
            using (FaultInjectionScope inner =
                FaultInjectionScope.Fail(
                    FaultInjectionPoint.HttpRequest))
            {
                ServiceResponse response =
                    await client.SendAsync(IranDirectCommand.Status);

                Assert.True(response.Success);
            }
        }

        Assert.Equal(1, factory.ConnectCallCount);
    }

    [Fact]
    public async Task SendAsync_ScopeDisposal_PreventsLeakage()
    {
        FakeNamedPipeClientFactory factory = CreateFactory(SuccessResponseLine());
        IranDirectServiceClient client = CreateClient(factory);

        FaultInjectionScope scope =
            FaultInjectionScope.Fail(
                FaultInjectionPoint.NamedPipeSend);
        scope.Dispose();

        ServiceResponse response =
            await client.SendAsync(IranDirectCommand.Status);

        Assert.True(response.Success);
        Assert.Equal(1, factory.ConnectCallCount);
    }

    [Fact]
    public async Task SendAsync_PreCanceledToken_PropagatesOperationCanceled()
    {
        using CancellationTokenSource callerCts = new();
        callerCts.Cancel();

        FakeNamedPipeClientFactory factory = new(
            cancellationToken =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult<INamedPipeClientConnection>(
                    new FakeNamedPipeClientConnection(
                        SuccessResponseLine()));
            });
        IranDirectServiceClient client = CreateClient(factory);

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => client.SendAsync(
                IranDirectCommand.Status,
                cancellationToken: callerCts.Token));

        Assert.Equal(1, factory.ConnectCallCount);
    }

    [Fact]
    public async Task SendAsync_ConnectTimeout_MapsToTimeoutException()
    {
        FakeNamedPipeClientFactory factory = new(
            cancellationToken =>
                throw new OperationCanceledException(
                    cancellationToken));
        IranDirectServiceClient client = CreateClient(factory);

        TimeoutException exception =
            await Assert.ThrowsAsync<TimeoutException>(
                () => client.SendAsync(IranDirectCommand.Status));

        Assert.Equal(
            "IranDirect Service is unavailable or did not " +
            "accept the connection within 5 seconds.",
            exception.Message);
        Assert.Equal(1, factory.ConnectCallCount);
    }

    [Fact]
    public async Task SendAsync_TimeoutMapping_DoesNotMaskCallerCancellation()
    {
        using CancellationTokenSource callerCts = new();
        callerCts.Cancel();

        FakeNamedPipeClientFactory factory = new(
            cancellationToken =>
                throw new OperationCanceledException(
                    cancellationToken));
        IranDirectServiceClient client = CreateClient(factory);

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => client.SendAsync(
                IranDirectCommand.Status,
                cancellationToken: callerCts.Token));

        Assert.Equal(1, factory.ConnectCallCount);
    }

    [Fact]
    public async Task SendAsync_RequestSerialization_UnchangedWithoutFaults()
    {
        FakeNamedPipeClientFactory factory = CreateFactory(SuccessResponseLine());
        IranDirectServiceClient client = CreateClient(factory);

        ServiceResponse response = await client.SendAsync(
            IranDirectCommand.CustomRoutesAddDomain,
            value: "example.com",
            description: "my site");

        Assert.True(response.Success);
        Assert.Equal(1, factory.ConnectCallCount);

        string expected = JsonSerializer.Serialize(
            new ServiceRequest
            {
                Command = IranDirectCommand.CustomRoutesAddDomain,
                Value = "example.com",
                Description = "my site"
            },
            IranDirectJson.Options);

        Assert.Equal(
            expected,
            factory.LastConnection!.WrittenLines.Single());
    }

    [Fact]
    public async Task SendAsync_NullJsonResponse_ReturnsInvalidResponseFailure()
    {
        FakeNamedPipeClientFactory factory = CreateFactory(
            responseLine: "null");
        IranDirectServiceClient client = CreateClient(factory);

        ServiceResponse response =
            await client.SendAsync(IranDirectCommand.Status);

        Assert.False(response.Success);
        Assert.Equal("INVALID_RESPONSE", response.ErrorCode);
        Assert.Equal(
            "The service returned an invalid response.",
            response.Message);
        Assert.Equal(1, factory.ConnectCallCount);
        Assert.Equal(1, factory.LastConnection!.ReadCallCount);
    }

    [Fact]
    public async Task SendAsync_EmptyResponseLine_ThrowsJsonException()
    {
        FakeNamedPipeClientFactory factory = new();
        IranDirectServiceClient client = CreateClient(factory);

        await Assert.ThrowsAsync<JsonException>(
            () => client.SendAsync(IranDirectCommand.Status));

        Assert.Equal(1, factory.ConnectCallCount);
        Assert.Equal(1, factory.LastConnection!.ReadCallCount);
    }

    [Fact]
    public async Task SendAsync_FaultedRequest_NeverReachesDispatcher_NextReachesExactlyOnce()
    {
        FakeDispatcher dispatcher = new();
        FakeNamedPipeClientFactory factory = CreateDispatcherFactory(dispatcher);
        IranDirectServiceClient client = CreateClient(factory);

        using (FaultInjectionScope scope =
            FaultInjectionScope.Fail(
                FaultInjectionPoint.NamedPipeSend))
        {
            await Assert.ThrowsAsync<FaultInjectionException>(
                () => client.SendAsync(IranDirectCommand.Status));
        }

        Assert.Empty(dispatcher.ReceivedRequests);
        Assert.Equal(0, factory.ConnectCallCount);

        ServiceResponse response =
            await client.SendAsync(IranDirectCommand.Status);

        Assert.True(response.Success);
        string line = Assert.Single(dispatcher.ReceivedRequests);
        ServiceRequest request =
            JsonSerializer.Deserialize<ServiceRequest>(
                line,
                IranDirectJson.Options)!;
        Assert.Equal(IranDirectCommand.Status, request.Command);
        Assert.Equal(1, factory.ConnectCallCount);
    }

    [Fact]
    public async Task SendAsync_DifferentCommands_AreUnaffectedByFaultMechanism()
    {
        IranDirectCommand[] commands =
        [
            IranDirectCommand.Status,
            IranDirectCommand.CustomRoutesList,
            IranDirectCommand.Enable,
            IranDirectCommand.UpdatePrefixes,
            IranDirectCommand.ExecutionPreview
        ];

        foreach (IranDirectCommand command in commands)
        {
            FakeNamedPipeClientFactory factory =
                CreateFactory(SuccessResponseLine());
            IranDirectServiceClient client = CreateClient(factory);

            using (FaultInjectionScope scope =
                FaultInjectionScope.Fail(
                    FaultInjectionPoint.NamedPipeSend))
            {
                await Assert.ThrowsAsync<FaultInjectionException>(
                    () => client.SendAsync(command));
            }

            Assert.Equal(0, factory.ConnectCallCount);
            Assert.Null(factory.LastConnection);

            ServiceResponse response = await client.SendAsync(command);

            Assert.True(response.Success);
            ServiceRequest sent =
                JsonSerializer.Deserialize<ServiceRequest>(
                    factory.LastConnection!.WrittenLines.Single(),
                    IranDirectJson.Options)!;
            Assert.Equal(command, sent.Command);
        }
    }

    [Fact]
    public async Task SendAsync_ConcurrentClients_IndependentPolicies_StayIsolated()
    {
        FakeNamedPipeClientFactory faultedFactory = new();
        FakeNamedPipeClientFactory healthyFactory =
            CreateFactory(SuccessResponseLine());

        IranDirectServiceClient faultedClient = CreateClient(
            faultedFactory,
            FaultInjectionPolicy.For(
                [FaultInjectionPoint.NamedPipeSend]));
        IranDirectServiceClient healthyClient = CreateClient(healthyFactory);

        Task<ServiceResponse> faultedTask =
            faultedClient.SendAsync(IranDirectCommand.Status);
        Task<ServiceResponse> healthyTask =
            healthyClient.SendAsync(IranDirectCommand.Enable);

        FaultInjectionException exception =
            await Assert.ThrowsAsync<FaultInjectionException>(
                () => faultedTask);

        ServiceResponse healthyResponse = await healthyTask;

        Assert.Equal(
            FaultInjectionPoint.NamedPipeSend,
            exception.Point);
        Assert.Equal(0, faultedFactory.ConnectCallCount);
        Assert.True(healthyResponse.Success);
        Assert.Equal(1, healthyFactory.ConnectCallCount);
        ServiceRequest sent =
            JsonSerializer.Deserialize<ServiceRequest>(
                healthyFactory.LastConnection!.WrittenLines.Single(),
                IranDirectJson.Options)!;
        Assert.Equal(IranDirectCommand.Enable, sent.Command);
    }

    [Fact]
    public async Task SendAsync_ScopeInChildTask_DoesNotLeakToParentContext()
    {
        await Task.Run(() =>
        {
            using (FaultInjectionScope scope =
                FaultInjectionScope.Fail(
                    FaultInjectionPoint.NamedPipeSend))
            {
                Assert.True(FaultInjectionScope.IsActive);
            }
        });

        Assert.False(FaultInjectionScope.IsActive);

        FakeNamedPipeClientFactory factory =
            CreateFactory(SuccessResponseLine());
        IranDirectServiceClient client = CreateClient(factory);

        ServiceResponse response =
            await client.SendAsync(IranDirectCommand.Status);

        Assert.True(response.Success);
        Assert.Equal(1, factory.ConnectCallCount);
    }

    [Fact]
    public async Task SendAsync_GatedRequest_UnrelatedConcurrentRequestSucceeds()
    {
        TaskCompletionSource releaseGate =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        FakeNamedPipeClientFactory gatedFactory = new(
            async cancellationToken =>
            {
                await releaseGate.Task;
                return new FakeNamedPipeClientConnection(
                    SuccessResponseLine());
            });
        FakeNamedPipeClientFactory immediateFactory =
            CreateFactory(SuccessResponseLine());

        IranDirectServiceClient gatedClient = CreateClient(gatedFactory);
        IranDirectServiceClient immediateClient =
            CreateClient(immediateFactory);

        Task<ServiceResponse> gatedTask =
            gatedClient.SendAsync(IranDirectCommand.Status);
        Task<ServiceResponse> immediateTask =
            immediateClient.SendAsync(IranDirectCommand.Status);

        ServiceResponse immediateResponse = await immediateTask;

        Assert.True(immediateResponse.Success);
        Assert.Equal(1, immediateFactory.ConnectCallCount);
        Assert.True(gatedFactory.ConnectCallCount == 1);
        Assert.False(gatedTask.IsCompleted);

        releaseGate.SetResult();
        ServiceResponse gatedResponse = await gatedTask;

        Assert.True(gatedResponse.Success);
        Assert.Equal(1, gatedFactory.ConnectCallCount);
    }

    [Fact]
    public async Task SendAsync_SuccessfulSend_DisposesConnection()
    {
        FakeNamedPipeClientFactory factory = CreateFactory(SuccessResponseLine());
        IranDirectServiceClient client = CreateClient(factory);

        await client.SendAsync(IranDirectCommand.Status);

        FakeNamedPipeClientConnection connection =
            factory.LastConnection!;
        Assert.Equal(1, connection.DisposeCount);
        Assert.True(connection.IsDisposed);
    }

    [Fact]
    public async Task SendAsync_ReadFailure_DisposesConnection()
    {
        FakeNamedPipeClientFactory factory = new(
            cancellationToken =>
                Task.FromResult<INamedPipeClientConnection>(
                    new FakeNamedPipeClientConnection(
                        throwOnRead: true)));
        IranDirectServiceClient client = CreateClient(factory);

        await Assert.ThrowsAsync<IOException>(
            () => client.SendAsync(IranDirectCommand.Status));

        FakeNamedPipeClientConnection connection =
            factory.LastConnection!;
        Assert.Equal(1, connection.DisposeCount);
        Assert.True(connection.IsDisposed);
        Assert.Equal(1, connection.WriteCallCount);
    }

    [Fact]
    public async Task SendAsync_WriteFailure_DisposesConnection()
    {
        FakeNamedPipeClientFactory factory = new(
            cancellationToken =>
                Task.FromResult<INamedPipeClientConnection>(
                    new FakeNamedPipeClientConnection(
                        throwOnWrite: true)));
        IranDirectServiceClient client = CreateClient(factory);

        await Assert.ThrowsAsync<IOException>(
            () => client.SendAsync(IranDirectCommand.Status));

        FakeNamedPipeClientConnection connection =
            factory.LastConnection!;
        Assert.Equal(1, connection.DisposeCount);
        Assert.True(connection.IsDisposed);
        Assert.Equal(1, connection.WriteCallCount);
        Assert.Equal(0, connection.ReadCallCount);
    }

    private static IranDirectServiceClient CreateClient(
        FakeNamedPipeClientFactory factory,
        IFaultInjectionPolicy? faultPolicy = null) =>
        new(factory, faultPolicy);

    private static FakeNamedPipeClientFactory CreateFactory(
        string responseLine) =>
        new(
            cancellationToken =>
                Task.FromResult<INamedPipeClientConnection>(
                    new FakeNamedPipeClientConnection(
                        responseLine)));

    private static FakeNamedPipeClientFactory CreateDispatcherFactory(
        FakeDispatcher dispatcher) =>
        new(
            cancellationToken =>
                Task.FromResult<INamedPipeClientConnection>(
                    new FakeNamedPipeClientConnection(
                        responseLine: SuccessResponseLine(),
                        dispatchTarget: dispatcher.ReceivedRequests)));

    private static string SuccessResponseLine() =>
        JsonSerializer.Serialize(
            new ServiceResponse
            {
                Success = true,
                Message = "ok."
            },
            IranDirectJson.Options);

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

        public FakeNamedPipeClientConnection? LastConnection
        {
            get;
            private set;
        }

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

        private static async Task<INamedPipeClientConnection>
            DefaultConnect(
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
            bool throwOnRead = false,
            List<string>? dispatchTarget = null)
        {
            _responseLine = responseLine;
            _throwOnWrite = throwOnWrite;
            _throwOnRead = throwOnRead;
            DispatchTarget = dispatchTarget;
        }

        public List<string> WrittenLines { get; } = [];

        public List<string>? DispatchTarget { get; }

        public int WriteCallCount { get; private set; }

        public int ReadCallCount { get; private set; }

        public int DisposeCount { get; private set; }

        public bool IsDisposed => _disposed != 0;

        public Task WriteRequestLineAsync(
            string requestJson,
            CancellationToken cancellationToken)
        {
            WriteCallCount++;
            WrittenLines.Add(requestJson);
            DispatchTarget?.Add(requestJson);

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

    private sealed class FakeDispatcher
    {
        public List<string> ReceivedRequests { get; } = [];
    }
}
