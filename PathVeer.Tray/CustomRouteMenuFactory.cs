namespace PathVeer.Tray;

public static class CustomRouteMenuFactory
{
    public static ToolStripMenuItem CreateCustomRoutesItem() =>
        new(CustomRouteMenuPolicy.MenuItemText);
}
