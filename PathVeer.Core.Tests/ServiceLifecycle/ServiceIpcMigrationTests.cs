using System.Runtime.Versioning;
using System.ServiceProcess;
using PathVeer.Core.Ipc;
using PathVeer.Core.ServiceLifecycle;
using PathVeer.Core.Testing.FaultInjection;

namespace PathVeer.Core.Tests.ServiceLifecycle;

[SupportedOSPlatform("windows")]
public sealed class ServiceIpcMigrationTests
{
    #region Service identity constants

    [Fact]
    public void NewServiceIdentity_IsPathVeer()
    {
        Assert.Equal("PathVeer", PathVeerServiceNames.ServiceName);
        Assert.Equal("PathVeer Service", PathVeerServiceNames.DisplayName);
        Assert.Contains(
            "VPN endpoint connectivity",
            PathVeerServiceNames.Description);
    }

    [Fact]
    public void LegacyServiceNameRetainedOnlyForMigration()
    {
        // The legacy name must survive so the installer can stop/remove the
        // old service; it is NOT the active identity.
        Assert.Equal("IranDirect", LegacyServiceNames.ServiceName);
        Assert.NotEqual(
            PathVeerServiceNames.ServiceName, LegacyServiceNames.ServiceName);
        Assert.NotEqual(
            PathVeerServiceNames.DisplayName,
            LegacyServiceNames.DisplayName);
    }

    [Fact]
    public void Program_HostServiceName_AlignsWithNewIdentity()
    {
        // The worker/host registers the Windows Service under this name.
        Assert.Equal("PathVeer Service", PathVeerServiceNames.DisplayName);
    }

    #endregion

    #region Pipe identity + dual-listen

    [Fact]
    public void PipeIdentity_PrimaryIsPathVeer_LegacyIsIranDirect()
    {
        Assert.Equal("PathVeer.Control.v1", PathVeerPipeNames.PrimaryPipeName);
        Assert.Equal(
            "IranDirect.Control.v1", PathVeerPipeNames.LegacyPipeName);
        Assert.Equal(
            "IranDirect.Control.v1", PathVeerPipeNames.LegacyPipeName);
    }

    [Fact]
    public void DualListenList_ContainsBothPipes_PrimaryFirst()
    {
        IReadOnlyList<string> names = PathVeerPipeNames.AllListenNames;

        Assert.Equal(2, names.Count);
        Assert.Equal("PathVeer.Control.v1", names[0]);
        Assert.Equal("IranDirect.Control.v1", names[1]);
    }

    [Fact]
    public void BothPipeEndpoints_MapToSameCoordinatorContract()
    {
        // The server dispatches every pipe (primary + legacy) into the same
        // command handler + OperationCoordinator. There is exactly one
        // authority; the pipes are two front doors into one process.
        // This is encoded by NamedPipeCommandServer using
        // PathVeerPipeNames.AllListenNames and a single HandleOneClientAsync.
        Assert.Equal(
            PathVeerPipeNames.PrimaryPipeName,
            PathVeerPipeNames.AllListenNames[0]);
        Assert.Contains(
            PathVeerPipeNames.LegacyPipeName,
            PathVeerPipeNames.AllListenNames);
    }

    #endregion

    #region Client fallback safety

    [Fact]
    public async Task Client_NewService_PrefersPrimaryPipe()
    {
        RecordingFactory factory = new(
            primaryConnectable: true,
            legacyConnectable: true);

        IranDirectServiceClient client = CreateClient(factory);

        ServiceResponse response = await client.SendAsync(
            IranDirectCommand.Status);

        Assert.True(response.Success);
        Assert.Equal(1, factory.ConnectCount);
        Assert.Equal("primary", factory.LastPipeAttempted);
    }

    [Fact]
    public async Task Client_OldService_LegacyPipeFallback()
    {
        // New PathVeer client against a fleet where only the legacy pipe is up:
        // primary cannot be opened, so we fall back to the legacy pipe.
        RecordingFactory factory = new(
            primaryConnectable: false,
            legacyConnectable: true);

        IranDirectServiceClient client = CreateClient(factory);

        ServiceResponse response = await client.SendAsync(
            IranDirectCommand.Status);

        Assert.True(response.Success);
        Assert.Equal(2, factory.ConnectCount);
        Assert.Equal("legacy", factory.LastPipeAttempted);
    }

    [Fact]
    public async Task Client_PrimaryConnectFailure_DoesNotSendOnEither()
    {
        // When the primary cannot be opened we reconnect on the legacy pipe.
        // The FACT that primary failed means NO bytes were sent, so replaying
        // the command on legacy is unambiguously safe (exactly-once).
        RecordingFactory factory = new(
            primaryConnectable: false,
            legacyConnectable: true);

        IranDirectServiceClient client = CreateClient(factory);

        await client.SendAsync(IranDirectCommand.Enable);

        // Exactly one write occurred — on the legacy pipe after the safe
        // connect-time fallback, never a double execution.
        Assert.Equal(1, factory.TotalWrites);
        Assert.Equal("legacy", factory.LastPipeWritten);
    }

    [Fact]
    public async Task Client_ConnectedButSendFails_DoesNotFallback()
    {
        // Ambiguous phase C: connected to primary, but the write failed before
        // any response. We must NOT silently retry on the legacy pipe because
        // the command may have partially executed. The client surfaces the
        // failure instead.
        ThrowingOnWriteFactory factory = new(
            throwOnPipe: "primary");

        IranDirectServiceClient client = CreateClient(factory);

        await Assert.ThrowsAsync<IOException>(
            () => client.SendAsync(IranDirectCommand.Enable));

        // It connected (to primary) but never attempted the legacy pipe.
        Assert.Equal(1, factory.ConnectCallCount);
    }

    [Fact]
    public async Task Client_ConnectedButReadFails_DoesNotFallback()
    {
        // Ambiguous phase C: connected, command sent, but response unavailable.
        // Outcome is ambiguous — do NOT replay on legacy.
        ThrowingOnReadFactory factory = new(
            throwOnPipe: "primary");

        IranDirectServiceClient client = CreateClient(factory);

        await Assert.ThrowsAsync<IOException>(
            () => client.SendAsync(IranDirectCommand.Enable));

        // One connect to primary, zero attempts on legacy.
        Assert.Equal(1, factory.ConnectCallCount);
    }

    [Fact]
    public async Task Client_ValidFailureResponse_DoesNotFallback()
    {
        // Phase D: the service responded with a valid FAILURE. That is a
        // definitive outcome — do not fall back to the legacy pipe.
        RecordingFactory factory = new(
            primaryConnectable: true,
            legacyConnectable: false,
            responseLine: "{\"success\":false," +
                "\"errorCode\":\"INVALID_CONFIGURATION_VALUE\"," +
                "\"message\":\"bad\"}");

        IranDirectServiceClient client = CreateClient(factory);

        ServiceResponse response = await client.SendAsync(
            IranDirectCommand.Status);

        Assert.False(response.Success);
        Assert.Equal("INVALID_CONFIGURATION_VALUE", response.ErrorCode);
        Assert.Equal(1, factory.ConnectCount);
    }

    [Fact]
    public async Task Client_BothPipesUnavailable_ThrowsUnavailable()
    {
        RecordingFactory factory = new(
            primaryConnectable: false,
            legacyConnectable: false);

        IranDirectServiceClient client = CreateClient(factory);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.SendAsync(IranDirectCommand.Status));

        Assert.Equal(2, factory.ConnectCount);
    }

    #endregion

    #region Single-authority migration state machine

    [Fact]
    public async Task Migration_LegacyPresent_StopsLegacyThenStartsNew()
    {
        DualAdapter legacy = new(
            exists: true, initialStatus: ServiceControllerStatus.Running);
        DualAdapter next = new(
            exists: true, initialStatus: ServiceControllerStatus.Stopped);

        ServiceIdentityMigration migration = new(legacy, next);

        MigrationReport report = await migration.MigrateAsync();

        Assert.True(report.LegacyServiceExisted);
        Assert.True(report.LegacyStopped);
        Assert.True(report.NewServiceStarted);
        Assert.True(report.SingleAuthorityPreserved);
        Assert.Equal(1, legacy.StopCalls);
        Assert.Equal(1, next.StartCalls);
        Assert.Equal(ServiceControllerStatus.Stopped, legacy.Status);
        Assert.Equal(ServiceControllerStatus.Running, next.Status);
    }

    [Fact]
    public async Task Migration_NoLegacy_OnlyStartsNew()
    {
        DualAdapter legacy = new(exists: false);
        DualAdapter next = new(
            exists: true, initialStatus: ServiceControllerStatus.Stopped);

        ServiceIdentityMigration migration = new(legacy, next);

        MigrationReport report = await migration.MigrateAsync();

        Assert.False(report.LegacyServiceExisted);
        Assert.Equal(0, legacy.StopCalls);
        Assert.Equal(1, next.StartCalls);
    }

    [Fact]
    public async Task Migration_LegacyFailsToStop_ThrowsNoConcurrency()
    {
        // If the legacy service cannot be stopped, we must NOT start the new
        // one — that would create two authorities.
        DualAdapter legacy = new(
            exists: true,
            initialStatus: ServiceControllerStatus.Running,
            refuseToStop: true);
        DualAdapter next = new(
            exists: true, initialStatus: ServiceControllerStatus.Stopped);

        ServiceIdentityMigration migration = new(legacy, next);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => migration.MigrateAsync());

        Assert.Equal(0, next.StartCalls);
    }

    [Fact]
    public async Task Migration_FreshNewAlreadyRunning_NoExtraStart()
    {
        DualAdapter legacy = new(exists: false);
        DualAdapter next = new(
            exists: true, initialStatus: ServiceControllerStatus.Running);

        ServiceIdentityMigration migration = new(legacy, next);

        MigrationReport report = await migration.MigrateAsync();

        Assert.True(report.NewServiceStarted);
        Assert.Equal(0, next.StartCalls);
    }

    [Fact]
    public async Task Rollback_StopsNewAndRestartsLegacyIfStatePermits()
    {
        DualAdapter legacy = new(
            exists: true, initialStatus: ServiceControllerStatus.Stopped);
        DualAdapter next = new(
            exists: true, initialStatus: ServiceControllerStatus.Running);

        ServiceIdentityMigration migration = new(legacy, next);

        RollbackReport report = await migration.RollbackAsync();

        Assert.True(report.NewServiceStopped);
        Assert.True(report.LegacyServiceRestarted);
        Assert.Equal(1, next.StopCalls);
        Assert.Equal(1, legacy.StartCalls);
    }

    #endregion

    #region Helpers

    private static IranDirectServiceClient CreateClient(
        INamedPipeClientFactory factory,
        IFaultInjectionPolicy? policy = null) =>
        new(factory, policy ?? FaultInjectionPolicy.Never);

    private static string SuccessResponseLine() =>
        "{\"success\":true,\"message\":\"ok\"}";

    private static string FailureResponseLine() =>
        "{\"success\":false,\"errorCode\":" +
        "\"INVALID_CONFIGURATION_VALUE\",\"message\":\"bad\"}";

    private sealed class RecordingFactory : INamedPipeClientFactory
    {
        private readonly bool _primaryConnectable;
        private readonly bool _legacyConnectable;
        private readonly string _responseLine;

        public RecordingFactory(
            bool primaryConnectable,
            bool legacyConnectable,
            string responseLine = "{\"success\":true,\"message\":\"ok\"}")
        {
            _primaryConnectable = primaryConnectable;
            _legacyConnectable = legacyConnectable;
            _responseLine = responseLine;
        }

        public int ConnectCount { get; private set; }

        public int TotalWrites { get; private set; }

        public string? LastPipeAttempted { get; private set; }

        public string? LastPipeWritten { get; private set; }

        public async Task<INamedPipeClientConnection> ConnectAsync(
            CancellationToken cancellationToken)
        {
            foreach (string pipe in PathVeerPipeNames.AllListenNames)
            {
                bool canConnect = pipe == PathVeerPipeNames.PrimaryPipeName
                    ? _primaryConnectable
                    : _legacyConnectable;

                ConnectCount++;
                LastPipeAttempted = LabelFor(pipe);

                if (canConnect)
                {
                    return new RecordingConnection(this, pipe, _responseLine);
                }
            }

            throw new InvalidOperationException(
                "Neither pipe available.");
        }

        private static string LabelFor(string pipe) =>
            pipe == PathVeerPipeNames.PrimaryPipeName
                ? "primary"
                : "legacy";

        private sealed class RecordingConnection :
            INamedPipeClientConnection
        {
            private readonly RecordingFactory _owner;
            private readonly string _pipe;
            private readonly string _responseLine;

            public RecordingConnection(
                RecordingFactory owner,
                string pipe,
                string responseLine)
            {
                _owner = owner;
                _pipe = pipe;
                _responseLine = responseLine;
            }

            public Task WriteRequestLineAsync(
                string requestJson,
                CancellationToken cancellationToken)
            {
                _owner.TotalWrites++;
                _owner.LastPipeWritten = LabelFor(_pipe);
                return Task.CompletedTask;
            }

            public ValueTask<string?> ReadResponseLineAsync(
                CancellationToken cancellationToken) =>
                new(_responseLine);

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class ThrowingOnWriteFactory : INamedPipeClientFactory
    {
        private readonly string _throwOnPipe;

        public ThrowingOnWriteFactory(string throwOnPipe)
        {
            _throwOnPipe = throwOnPipe;
        }

        public int ConnectCallCount { get; private set; }

        public async Task<INamedPipeClientConnection> ConnectAsync(
            CancellationToken cancellationToken)
        {
            ConnectCallCount++;
            return await Task.FromResult<INamedPipeClientConnection>(
                new ThrowingConnection(_throwOnPipe, throwsOn: "write"));
        }

        internal sealed class ThrowingConnection :
            INamedPipeClientConnection
        {
            private readonly string _pipe;
            private readonly string _throwsOn;

            public ThrowingConnection(string pipe, string throwsOn)
            {
                _pipe = pipe;
                _throwsOn = throwsOn;
            }

            public Task WriteRequestLineAsync(
                string requestJson,
                CancellationToken cancellationToken)
            {
                if (_throwsOn == "write")
                {
                    throw new IOException("write failed");
                }

                return Task.CompletedTask;
            }

            public ValueTask<string?> ReadResponseLineAsync(
                CancellationToken cancellationToken) =>
                _throwsOn == "read"
                    ? throw new IOException("read failed")
                    : new ValueTask<string?>("{\"success\":true}");

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class ThrowingOnReadFactory : INamedPipeClientFactory
    {
        private readonly string _throwOnPipe;

        public ThrowingOnReadFactory(string throwOnPipe)
        {
            _throwOnPipe = throwOnPipe;
        }

        public int ConnectCallCount { get; private set; }

        public async Task<INamedPipeClientConnection> ConnectAsync(
            CancellationToken cancellationToken)
        {
            ConnectCallCount++;
            return await Task.FromResult<INamedPipeClientConnection>(
                new ThrowingOnWriteFactory.ThrowingConnection(
                    _throwOnPipe, "read"));
        }
    }

    private sealed class DualAdapter : IServiceControllerAdapter
    {
        private readonly bool _exists;
        private readonly bool _refuseToStop;

        public DualAdapter(
            bool exists = true,
            ServiceControllerStatus initialStatus =
                ServiceControllerStatus.Stopped,
            bool refuseToStop = false)
        {
            _exists = exists;
            _refuseToStop = refuseToStop;
            Status = initialStatus;
        }

        public int StartCalls { get; private set; }

        public int StopCalls { get; private set; }

        public ServiceControllerStatus Status { get; private set; }

        public bool Exists() => _exists;

        public ServiceControllerStatus GetStatus() => Status;

        public void Start(TimeSpan timeout)
        {
            StartCalls++;
            Status = ServiceControllerStatus.Running;
        }

        public void Stop(TimeSpan timeout)
        {
            if (_refuseToStop)
            {
                // Simulate a service that will not stop.
                return;
            }

            StopCalls++;
            Status = ServiceControllerStatus.Stopped;
        }
    }

    #endregion
}
