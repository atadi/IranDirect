namespace PathVeer.Core.ServiceLifecycle;

using System.Runtime.Versioning;
using System.ServiceProcess;

[SupportedOSPlatform("windows")]
public sealed class WindowsServiceControllerAdapter :
    IServiceControllerAdapter
{
    private readonly string _serviceName;

    public WindowsServiceControllerAdapter(string serviceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);
        _serviceName = serviceName;
    }

    public bool Exists() => TryCreate() is not null;

    public ServiceControllerStatus GetStatus()
    {
        using ServiceController controller = CreateRequired();
        controller.Refresh();
        return controller.Status;
    }

    public void Start(TimeSpan timeout)
    {
        using ServiceController controller = CreateRequired();
        controller.Refresh();

        if (controller.Status == ServiceControllerStatus.Running)
            return;

        controller.Start();
        controller.WaitForStatus(
            ServiceControllerStatus.Running, timeout);
    }

    public void Stop(TimeSpan timeout)
    {
        using ServiceController controller = CreateRequired();
        controller.Refresh();

        if (controller.Status == ServiceControllerStatus.Stopped)
            return;

        controller.Stop();
        controller.WaitForStatus(
            ServiceControllerStatus.Stopped, timeout);
    }

    private ServiceController? TryCreate()
    {
        ServiceController controller = new(_serviceName);

        try
        {
            _ = controller.Status;
            return controller;
        }
        catch (InvalidOperationException)
        {
            controller.Dispose();
            return null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            controller.Dispose();
            return null;
        }
    }

    private ServiceController CreateRequired() =>
        TryCreate()
        ?? throw new InvalidOperationException(
            $"Service '{_serviceName}' is not installed.");
}
