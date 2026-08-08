using PathVeer.Tray;

namespace PathVeer.Core.Tests.Tray;

public sealed class ExecutionPreviewMenuPolicyTests
{
    [Theory]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    public void IsAvailable_RequiresRunningAndNotBusy(
        bool serviceRunning,
        bool busy,
        bool expected)
    {
        Assert.Equal(
            expected,
            ExecutionPreviewMenuPolicy.IsAvailable(
                serviceRunning,
                busy));
    }

    [Fact]
    public void MenuItemText_IsStable()
    {
        Assert.Equal(
            "Preview Next Repair...",
            ExecutionPreviewMenuPolicy.MenuItemText);
    }
}
