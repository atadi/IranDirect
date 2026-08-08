namespace PathVeer.Tray;

public static class PrefixUpdateMenuPolicy
{
    public const string MenuItemText =
        "Check for Prefix Updates";

    public static bool IsAvailable(
        bool serviceRunning,
        bool busy) =>
        serviceRunning && !busy;
}
