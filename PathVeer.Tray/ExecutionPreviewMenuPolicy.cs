namespace PathVeer.Tray;

public static class ExecutionPreviewMenuPolicy
{
    public const string MenuItemText =
        "Preview Next Repair...";

    public static bool IsAvailable(
        bool serviceRunning,
        bool busy) =>
        serviceRunning && !busy;
}
