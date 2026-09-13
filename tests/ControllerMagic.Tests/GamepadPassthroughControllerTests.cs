using ControllerMagic;
using Xunit;

namespace ControllerMagic.Tests;

public class GamepadPassthroughControllerTests
{
    [Theory]
    [InlineData(true, true, false, true)]
    [InlineData(true, true, true, false)]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, false, false, false)]
    [InlineData(false, true, true, false)]
    [InlineData(false, false, true, false)]
    [InlineData(true, false, true, false)]
    public void ComputeShouldBeActive_AllCombinations_RequiresSettingOnDriversReadyAndNotSuspended(
        bool settingOn, bool driversReady, bool fullscreenSuspended, bool expected)
    {
        bool actual = GamepadPassthroughController.ComputeShouldBeActive(settingOn, driversReady, fullscreenSuspended);

        Assert.Equal(expected, actual);
    }
}
