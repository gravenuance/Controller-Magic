using ControllerMagic;
using Xunit;

namespace ControllerMagic.Tests;

public class ControllerLightsTests
{
    private static readonly ControllerMode[] LitModes = [ControllerMode.Mouse, ControllerMode.Keyboard, ControllerMode.LowBattery];

    [Theory]
    [InlineData(false, false, nameof(BatteryLevel.Medium), nameof(ControllerMode.Mouse))]
    [InlineData(false, true, nameof(BatteryLevel.Medium), nameof(ControllerMode.Keyboard))]
    [InlineData(true, false, nameof(BatteryLevel.Medium), nameof(ControllerMode.Suspended))]
    [InlineData(true, true, nameof(BatteryLevel.Medium), nameof(ControllerMode.Suspended))]
    [InlineData(false, false, nameof(BatteryLevel.Low), nameof(ControllerMode.LowBattery))]
    [InlineData(false, false, nameof(BatteryLevel.Empty), nameof(ControllerMode.LowBattery))]
    [InlineData(false, true, nameof(BatteryLevel.Low), nameof(ControllerMode.Keyboard))]
    [InlineData(true, false, nameof(BatteryLevel.Empty), nameof(ControllerMode.Suspended))]
    [InlineData(false, false, nameof(BatteryLevel.Full), nameof(ControllerMode.Mouse))]
    [InlineData(false, false, nameof(BatteryLevel.Wired), nameof(ControllerMode.Mouse))]
    [InlineData(false, false, nameof(BatteryLevel.Unknown), nameof(ControllerMode.Mouse))]
    public void ComputeMode_GameThenKeyboardThenLowBatteryThenMouse(bool blockedFullscreen, bool keyboardMode, string battery, string expected)
    {
        var mode = ControllerLights.ComputeMode(blockedFullscreen, keyboardMode, Enum.Parse<BatteryLevel>(battery));

        Assert.Equal(expected, mode.ToString());
    }

    // The game sets its own colour while it has the pad.
    [Fact]
    public void LightbarFor_Suspended_LeavesTheLightbarAlone() =>
        Assert.Null(ControllerLights.LightbarFor(ControllerMode.Suspended));

    [Fact]
    public void LightbarFor_EveryLitModeHasItsOwnColour()
    {
        var colors = LitModes.Select(ControllerLights.LightbarFor).ToList();

        Assert.DoesNotContain(null, colors);
        Assert.Equal(colors.Count, colors.Distinct().Count());
    }

    [Fact]
    public void LightbarFor_EveryColourIsFullySaturated_SoTheLightbarDoesNotWashItOutToWhite()
    {
        foreach (var mode in LitModes)
        {
            var c = ControllerLights.LightbarFor(mode)!.Value;
            Assert.True(Math.Min(c.R, Math.Min(c.G, c.B)) == 0, $"{mode} colour {c} has no channel off");
        }
    }

    [Theory]
    [InlineData(nameof(ControllerMode.Mouse), 0xFF, 0x60, 0x00)]
    [InlineData(nameof(ControllerMode.Keyboard), 0x00, 0xFF, 0x40)]
    [InlineData(nameof(ControllerMode.LowBattery), 0xFF, 0x00, 0x00)]
    public void LightbarFor_KeepsEachModesHueButNoBrighterThanSdlsOwnColours(string mode, int r, int g, int b)
    {
        var c = ControllerLights.LightbarFor(Enum.Parse<ControllerMode>(mode))!.Value;

        Assert.Equal(0x40, Math.Max(c.R, Math.Max(c.G, c.B)));
        Assert.Equal(Color.FromArgb(r, g, b).GetHue(), c.GetHue(), 1.5f);
    }

    [Fact]
    public void ScaleToPeak_ScalesEveryChannelByTheSameFactor()
    {
        var c = ControllerLights.ScaleToPeak(Color.FromArgb(0xC0, 0x60, 0x30), 0x40);

        Assert.Equal(Color.FromArgb(0x40, 0x20, 0x10).ToArgb(), c.ToArgb());
    }

    [Fact]
    public void PlayerLightsOff_LightsNoneOfTheFiveLeds() =>
        Assert.Equal(0, ControllerLights.PlayerLightsOff);
}
