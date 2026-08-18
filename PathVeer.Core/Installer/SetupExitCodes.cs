namespace PathVeer.Core.Installer;

/// <summary>
/// Stable setup exit codes, mirrored from the PowerShell deployment contract
/// (Install-PathVeer.ps1 -> Map-CategoryToExitCode). The bootstrapper returns
/// these so enterprise/CI automation can branch on outcome deterministically.
/// </summary>
public static class SetupExitCodes
{
    public const int Success = 0;
    public const int UserCancelled = 100;
    public const int ElevationDenied = 101;
    public const int InvalidArguments = 102;
    public const int DowngradeBlocked = 103;
    public const int PackageVerificationFailed = 104;
    public const int LegacyUnsupported = 105;
    public const int ServiceFailed = 106;
    public const int ReadinessFailed = 107;
    public const int UninstallFailed = 108;
    public const int PurgeFailed = 109;

    /// <summary>Generic / unexpected failure (script threw without a mapped category).</summary>
    public const int GenericFailure = 1;

    /// <summary>Bootstrapper-level failures (never reached the deployment script).</summary>
    public const int PackageDirectoryNotFound = 2;
    public const int ScriptResourceMissing = 3;
    public const int PowerShellUnavailable = 4;
    public const int RelaunchFailed = 5;
    public const int RuntimePrerequisiteMissing = 110;

    /// <summary>
    /// Maps a <see cref="System.Security.Principal.WindowsPrincipal"/> admin
    /// check failure into the stable elevation-denied contract. The bootstrapper
    /// catches the runas <see cref="System.ComponentModel.Win32Exception"/> and
    /// routes through this so the denial is not confused with a generic argument
    /// error (102) or relaunch failure (5).
    /// </summary>
    public static int FromElevationDenied() => ElevationDenied;

    /// <summary>
    /// Single source of truth mapping the deployment script's result
    /// <c>category</c> (written to -ResultFile) to a stable exit code. Both
    /// <c>InstallController</c> and <c>InstallForm</c> delegate here so the
    /// console, the unattended path, and the interactive UI can never disagree
    /// on which exit code a given failure category produces.
    /// </summary>
    public static int MapResultCategory(string category) => category switch
    {
        "UserCancelled" => UserCancelled,
        "ElevationDenied" => ElevationDenied,
        "InvalidArguments" => InvalidArguments,
        "DowngradeBlocked" => DowngradeBlocked,
        "PackageVerificationFail" => PackageVerificationFailed,
        "LegacyUnsupported" => LegacyUnsupported,
        "ServiceFailed" => ServiceFailed,
        "ReadinessFailed" => ReadinessFailed,
        "UninstallFailed" => UninstallFailed,
        "PurgeFailed" => PurgeFailed,
        _ => GenericFailure,
    };

    public static string Describe(int code) => code switch
    {
        Success => "PathVeer was installed successfully.",
        UserCancelled => "Installation was cancelled by the user.",
        ElevationDenied => "Administrator permission was not granted. Setup cannot continue.",
        InvalidArguments => "Setup was started with invalid arguments.",
        DowngradeBlocked => "A newer version of PathVeer is already installed. Setup cannot install an older version over it.",
        PackageVerificationFailed => "The installation package failed integrity verification. No changes were made.",
        LegacyUnsupported => "Setup detected an unsupported older IranDirect installation. No changes were made.",
        ServiceFailed => "PathVeer could not start its background service. Your existing settings were preserved.",
        ReadinessFailed => "PathVeer was installed, but readiness verification failed. Setup has not reported the upgrade as successful.",
        UninstallFailed => "PathVeer could not be fully removed. Some components may remain.",
        PurgeFailed => "PathVeer was removed but saved state could not be deleted.",
        PackageDirectoryNotFound => "The PathVeer package directory could not be located.",
        ScriptResourceMissing => "The embedded deployment script was not found. The installer is corrupt.",
        PowerShellUnavailable => "PowerShell could not be started to run the installer.",
        RelaunchFailed => "Setup could not restart with administrator privileges.",
        RuntimePrerequisiteMissing => "The required .NET runtime is not installed. PathVeer cannot run until it is present.",
        _ => "Setup finished with an unexpected error.",
    };
}
