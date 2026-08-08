namespace PathVeer.Core.ServiceLifecycle;

using System.Runtime.Versioning;
using System.ServiceProcess;

[SupportedOSPlatform("windows")]
public sealed record ServiceStatusSnapshot
{
    public required bool Installed { get; init; }

    public required bool Running { get; init; }

    public ServiceControllerStatus? Status { get; init; }

    public bool CanStart =>
        Installed && Status == ServiceControllerStatus.Stopped;

    public bool CanStop =>
        Installed && Status == ServiceControllerStatus.Running;

    public bool CanRestart => CanStop;

    public bool IsTransitional =>
        Status is ServiceControllerStatus.StartPending
            or ServiceControllerStatus.StopPending
            or ServiceControllerStatus.PausePending
            or ServiceControllerStatus.ContinuePending;

    public static ServiceStatusSnapshot NotInstalled { get; } =
        new()
        {
            Installed = false,
            Running = false,
            Status = null
        };
}
