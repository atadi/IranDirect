namespace PathVeer.Core.ServiceLifecycle;

using System.Diagnostics;
using System.Runtime.Versioning;
using System.ServiceProcess;

/// <summary>
/// Windows SCM implementation of <see cref="IServiceLifecycle"/>.
/// Depends on <see cref="IServiceControllerAdapter"/> so the logic
/// can be unit tested without a real service.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsServiceLifecycle : IServiceLifecycle
{
    private static readonly TimeSpan StateChangeTimeout =
        TimeSpan.FromSeconds(30);

    private readonly IServiceControllerAdapter _adapter;
    private readonly string _serviceName;
    private readonly string? _serviceBinaryPath;

    public WindowsServiceLifecycle(
        IServiceControllerAdapter adapter,
        string serviceName,
        string? serviceBinaryPath = null)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        _adapter = adapter;
        _serviceName = serviceName;
        _serviceBinaryPath = serviceBinaryPath;
    }

    public ServiceStatusSnapshot GetStatus()
    {
        if (!_adapter.Exists())
            return ServiceStatusSnapshot.NotInstalled;

        ServiceControllerStatus status = _adapter.GetStatus();

        return new ServiceStatusSnapshot
        {
            Installed = true,
            Running = status == ServiceControllerStatus.Running,
            Status = status
        };
    }

    public async Task InstallAsync(
        CancellationToken cancellationToken = default)
    {
        if (GetStatus().Installed)
            return;

        if (_serviceBinaryPath is null ||
            !File.Exists(_serviceBinaryPath))
        {
            throw new InvalidOperationException(
                "PathVeer.Service.exe was not found next to the " +
                "tray application. Run " +
                "tools\\Install.ps1 from an " +
                "elevated PowerShell instead.");
        }

        string quotedPath = $"\"{_serviceBinaryPath}\"";

        await RunScAsync(
            ["create", _serviceName,
             "binPath=", quotedPath,
             "start=", "delayed-auto",
             "displayName=", PathVeerServiceNames.DisplayName],
            cancellationToken);

        await RunScAsync(
            ["description", _serviceName,
             PathVeerServiceNames.Description],
            cancellationToken);

        await RunScAsync(
            ["failure", _serviceName,
             "reset=", "86400",
             "actions=",
             "restart/10000/restart/30000/restart/60000"],
            cancellationToken);

        await RunScAsync(
            ["failureflag", _serviceName, "1"],
            cancellationToken);
    }

    public async Task UninstallAsync(
        CancellationToken cancellationToken = default)
    {
        if (!GetStatus().Installed)
            return;

        await StopAsync(cancellationToken);

        await RunScAsync(
            ["delete", _serviceName],
            cancellationToken);
    }

    public Task StartAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ServiceStatusSnapshot snapshot = GetStatus();

        if (!snapshot.Installed)
        {
            throw new InvalidOperationException(
                $"Service '{_serviceName}' is not installed.");
        }

        if (snapshot.Running)
            return Task.CompletedTask;

        _adapter.Start(StateChangeTimeout);
        return Task.CompletedTask;
    }

    public Task StopAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ServiceStatusSnapshot snapshot = GetStatus();

        if (!snapshot.Installed)
        {
            throw new InvalidOperationException(
                $"Service '{_serviceName}' is not installed.");
        }

        if (snapshot.Status == ServiceControllerStatus.Stopped)
            return Task.CompletedTask;

        _adapter.Stop(StateChangeTimeout);
        return Task.CompletedTask;
    }

    public async Task RestartAsync(
        CancellationToken cancellationToken = default)
    {
        await StopAsync(cancellationToken);
        await StartAsync(cancellationToken);
    }

    private static async Task RunScAsync(
        string[] arguments,
        CancellationToken cancellationToken)
    {
        ProcessStartInfo startInfo = new("sc.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (string argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException(
                "Failed to start sc.exe.");

        string stdout = await process.StandardOutput
            .ReadToEndAsync(cancellationToken);
        string stderr = await process.StandardError
            .ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"sc.exe {string.Join(' ', arguments)} failed " +
                $"with exit code {process.ExitCode}. " +
                $"{stdout} {stderr}".Trim());
        }
    }
}
