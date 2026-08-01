namespace IranDirect.Tray;

public static class CustomRouteMenuFactory
{
    public static ToolStripMenuItem CreateCustomRoutesItem() =>
        new(CustomRouteMenuPolicy.MenuItemText);
}
