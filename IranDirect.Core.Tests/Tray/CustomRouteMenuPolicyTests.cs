using System.Windows.Forms;
using IranDirect.Tray;

namespace IranDirect.Core.Tests.Tray;

public sealed class CustomRouteMenuPolicyTests
{
    [Fact]
    public void IsAvailable_WhenRunningAndNotBusy_ReturnsTrue()
    {
        Assert.True(
            CustomRouteMenuPolicy.IsAvailable(
                serviceRunning: true,
                busy: false));
    }

    [Fact]
    public void IsAvailable_WhenBusy_ReturnsFalse()
    {
        Assert.False(
            CustomRouteMenuPolicy.IsAvailable(
                serviceRunning: true,
                busy: true));
    }

    [Fact]
    public void IsAvailable_WhenServiceNotRunning_ReturnsFalse()
    {
        Assert.False(
            CustomRouteMenuPolicy.IsAvailable(
                serviceRunning: false,
                busy: false));
    }

    [Fact]
    public void CreateCustomRoutesItem_HasExpectedText()
    {
        ToolStripMenuItem item =
            CustomRouteMenuFactory.CreateCustomRoutesItem();

        Assert.Equal(
            CustomRouteMenuPolicy.MenuItemText,
            item.Text);
    }
}
