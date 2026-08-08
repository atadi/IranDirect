namespace PathVeer.Core.Tests.ServiceLifecycle;

using System.Runtime.Versioning;
using System.ServiceProcess;
using PathVeer.Core.ServiceLifecycle;

[SupportedOSPlatform("windows")]
public sealed class WindowsServiceLifecycleTests
{
    [Fact]
    public void GetStatus_NotInstalled_ReturnsNotInstalledSnapshot()
    {
        FakeAdapter adapter = new() { ExistsResult = false };
        WindowsServiceLifecycle lifecycle = CreateLifecycle(adapter);

        ServiceStatusSnapshot snapshot = lifecycle.GetStatus();

        Assert.False(snapshot.Installed);
        Assert.False(snapshot.Running);
        Assert.Null(snapshot.Status);
        Assert.False(snapshot.CanStart);
        Assert.False(snapshot.CanStop);
        Assert.False(snapshot.CanRestart);
    }

    [Fact]
    public void GetStatus_Stopped_ReturnsInstalledNotRunning()
    {
        FakeAdapter adapter = new()
        {
            StatusResult = ServiceControllerStatus.Stopped
        };
        WindowsServiceLifecycle lifecycle = CreateLifecycle(adapter);

        ServiceStatusSnapshot snapshot = lifecycle.GetStatus();

        Assert.True(snapshot.Installed);
        Assert.False(snapshot.Running);
        Assert.Equal(
            ServiceControllerStatus.Stopped, snapshot.Status);
        Assert.True(snapshot.CanStart);
        Assert.False(snapshot.CanStop);
        Assert.False(snapshot.CanRestart);
        Assert.False(snapshot.IsTransitional);
    }

    [Fact]
    public void GetStatus_Running_ReturnsRunning()
    {
        FakeAdapter adapter = new()
        {
            StatusResult = ServiceControllerStatus.Running
        };
        WindowsServiceLifecycle lifecycle = CreateLifecycle(adapter);

        ServiceStatusSnapshot snapshot = lifecycle.GetStatus();

        Assert.True(snapshot.Installed);
        Assert.True(snapshot.Running);
        Assert.False(snapshot.CanStart);
        Assert.True(snapshot.CanStop);
        Assert.True(snapshot.CanRestart);
    }

    [Fact]
    public void GetStatus_StartPending_IsTransitional()
    {
        FakeAdapter adapter = new()
        {
            StatusResult = ServiceControllerStatus.StartPending
        };
        WindowsServiceLifecycle lifecycle = CreateLifecycle(adapter);

        ServiceStatusSnapshot snapshot = lifecycle.GetStatus();

        Assert.True(snapshot.Installed);
        Assert.False(snapshot.Running);
        Assert.False(snapshot.CanStart);
        Assert.False(snapshot.CanStop);
        Assert.True(snapshot.IsTransitional);
    }

    [Fact]
    public async Task StartAsync_WhenStopped_CallsAdapterStart()
    {
        FakeAdapter adapter = new()
        {
            StatusResult = ServiceControllerStatus.Stopped
        };
        WindowsServiceLifecycle lifecycle = CreateLifecycle(adapter);

        await lifecycle.StartAsync();

        Assert.Equal(1, adapter.StartCalls);
    }

    [Fact]
    public async Task StartAsync_WhenAlreadyRunning_DoesNotStart()
    {
        FakeAdapter adapter = new()
        {
            StatusResult = ServiceControllerStatus.Running
        };
        WindowsServiceLifecycle lifecycle = CreateLifecycle(adapter);

        await lifecycle.StartAsync();

        Assert.Equal(0, adapter.StartCalls);
    }

    [Fact]
    public async Task StartAsync_WhenNotInstalled_Throws()
    {
        FakeAdapter adapter = new() { ExistsResult = false };
        WindowsServiceLifecycle lifecycle = CreateLifecycle(adapter);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => lifecycle.StartAsync());

        Assert.Equal(0, adapter.StartCalls);
    }

    [Fact]
    public async Task StopAsync_WhenRunning_CallsAdapterStop()
    {
        FakeAdapter adapter = new()
        {
            StatusResult = ServiceControllerStatus.Running
        };
        WindowsServiceLifecycle lifecycle = CreateLifecycle(adapter);

        await lifecycle.StopAsync();

        Assert.Equal(1, adapter.StopCalls);
    }

    [Fact]
    public async Task StopAsync_WhenAlreadyStopped_DoesNotStop()
    {
        FakeAdapter adapter = new()
        {
            StatusResult = ServiceControllerStatus.Stopped
        };
        WindowsServiceLifecycle lifecycle = CreateLifecycle(adapter);

        await lifecycle.StopAsync();

        Assert.Equal(0, adapter.StopCalls);
    }

    [Fact]
    public async Task StopAsync_WhenNotInstalled_Throws()
    {
        FakeAdapter adapter = new() { ExistsResult = false };
        WindowsServiceLifecycle lifecycle = CreateLifecycle(adapter);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => lifecycle.StopAsync());

        Assert.Equal(0, adapter.StopCalls);
    }

    [Fact]
    public async Task RestartAsync_StopsThenStartsInOrder()
    {
        FakeAdapter adapter = new()
        {
            StatusResult = ServiceControllerStatus.Running
        };
        WindowsServiceLifecycle lifecycle = CreateLifecycle(adapter);

        await lifecycle.RestartAsync();

        Assert.Equal(1, adapter.StopCalls);
        Assert.Equal(1, adapter.StartCalls);
        Assert.Equal(
            ["stop", "start"], adapter.CallOrder);
    }

    [Fact]
    public async Task StartAsync_Cancelled_ThrowsBeforeTouchingService()
    {
        FakeAdapter adapter = new();
        WindowsServiceLifecycle lifecycle = CreateLifecycle(adapter);
        using CancellationTokenSource cts = new();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => lifecycle.StartAsync(cts.Token));

        Assert.Equal(0, adapter.StartCalls);
    }

    [Fact]
    public async Task InstallAsync_WhenAlreadyInstalled_DoesNothing()
    {
        FakeAdapter adapter = new();
        WindowsServiceLifecycle lifecycle = CreateLifecycle(adapter);

        await lifecycle.InstallAsync();

        Assert.Equal(0, adapter.StartCalls);
        Assert.Equal(0, adapter.StopCalls);
    }

    [Fact]
    public async Task InstallAsync_WhenNotInstalledAndNoBinary_Throws()
    {
        FakeAdapter adapter = new() { ExistsResult = false };
        WindowsServiceLifecycle lifecycle = new(
            adapter,
            PathVeerServiceNames.ServiceName,
            serviceBinaryPath: null);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => lifecycle.InstallAsync());
    }

    private static WindowsServiceLifecycle CreateLifecycle(
        FakeAdapter adapter) =>
        new(adapter, PathVeerServiceNames.ServiceName);

    private sealed class FakeAdapter : IServiceControllerAdapter
    {
        public bool ExistsResult { get; set; } = true;

        public ServiceControllerStatus StatusResult { get; set; } =
            ServiceControllerStatus.Stopped;

        public int StartCalls { get; private set; }

        public int StopCalls { get; private set; }

        public List<string> CallOrder { get; } = [];

        public bool Exists() => ExistsResult;

        public ServiceControllerStatus GetStatus() => StatusResult;

        public void Start(TimeSpan timeout)
        {
            StartCalls++;
            CallOrder.Add("start");
            StatusResult = ServiceControllerStatus.Running;
        }

        public void Stop(TimeSpan timeout)
        {
            StopCalls++;
            CallOrder.Add("stop");
            StatusResult = ServiceControllerStatus.Stopped;
        }
    }
}
