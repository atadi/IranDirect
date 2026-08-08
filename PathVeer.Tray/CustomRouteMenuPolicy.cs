namespace PathVeer.Tray;

public static class CustomRouteMenuPolicy
{
    public const string MenuItemText = "Custom Routes...";

    public static bool IsAvailable(
        bool serviceRunning,
        bool busy) =>
        serviceRunning && !busy;
}
