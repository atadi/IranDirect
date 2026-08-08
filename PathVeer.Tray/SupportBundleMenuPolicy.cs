namespace PathVeer.Tray;

public static class SupportBundleMenuPolicy
{
    public const string MenuItemText =
        "Create Support Bundle...";

    public static bool IsAvailable(
        bool serviceRunning,
        bool busy) =>
        serviceRunning && !busy;
}
