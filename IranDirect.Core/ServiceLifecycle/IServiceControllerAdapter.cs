namespace IranDirect.Core.ServiceLifecycle;

using System.Runtime.Versioning;
using System.ServiceProcess;

/// <summary>
/// Minimal abstraction over System.ServiceProcess.ServiceController
/// so service lifecycle logic can be unit tested without a real
/// Windows Service installed.
/// </summary>
[SupportedOSPlatform("windows")]
public interface IServiceControllerAdapter
{
    bool Exists();

    ServiceControllerStatus GetStatus();

    void Start(TimeSpan timeout);

    void Stop(TimeSpan timeout);
}
