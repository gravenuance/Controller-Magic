using ControllerMagic;
using Xunit;

namespace ControllerMagic.Tests;

public class LightbarTests
{
    [Theory]
    [InlineData(false, false, nameof(ControllerMode.Mouse))]
    [InlineData(false, true, nameof(ControllerMode.Keyboard))]
    [InlineData(true, false, nameof(ControllerMode.Suspended))]
    [InlineData(true, true, nameof(ControllerMode.Suspended))]
    public void ComputeMode_FullscreenSuspensionWinsOverKeyboardMode(bool blockedFullscreen, bool keyboardMode, string expected)
    {
        Assert.Equal(expected, Lightbar.ComputeMode(blockedFullscreen, keyboardMode).ToString());
    }

    [Fact]
    public void ColorFor_EveryModeHasItsOwnColour()
    {
        var colors = Enum.GetValues<ControllerMode>().Select(Lightbar.ColorFor).ToList();

        Assert.Equal(colors.Count, colors.Distinct().Count());
    }
}
