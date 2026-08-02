using IranDirect.Tray;

namespace IranDirect.Core.Tests.Tray;

public sealed class PrefixUpdateMenuPolicyTests
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
            PrefixUpdateMenuPolicy.IsAvailable(
                serviceRunning,
                busy));
    }

    [Fact]
    public void MenuItemText_IsStable()
    {
        Assert.Equal(
            "Check for Prefix Updates",
            PrefixUpdateMenuPolicy.MenuItemText);
    }
}
