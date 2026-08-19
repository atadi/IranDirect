using PathVeer.Core.Installer;

namespace PathVeer.Tray;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // Intrinsic per-interactive-user-session single-instance guard.
        // The Tray is per-user UI; this is the authority — NOT the installer,
        // autorun, or Start-Menu shortcut. A duplicate launch detects the live
        // owner, asks it to surface itself, and exits cleanly (no error dialog).
        using var guard = TraySingleInstance.TryAcquire();
        if (guard is null)
        {
            TraySingleInstance.SignalExisting();
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApplicationContext());
    }
}
