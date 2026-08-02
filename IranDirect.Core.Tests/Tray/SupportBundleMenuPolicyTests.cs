using IranDirect.Core.Ipc;
using IranDirect.Tray;

namespace IranDirect.Core.Tests.Tray;

public sealed class SupportBundleMenuPolicyTests
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
            SupportBundleMenuPolicy.IsAvailable(
                serviceRunning,
                busy));
    }

    [Fact]
    public void MenuItemText_IsStable()
    {
        Assert.Equal(
            "Create Support Bundle...",
            SupportBundleMenuPolicy.MenuItemText);
    }
}
