using System.Numerics;
using ControllerMagic;
using Xunit;

namespace ControllerMagic.Tests;

public class ControllerLightsTests
{
    [Theory]
    [InlineData(false, false, nameof(ControllerMode.Mouse))]
    [InlineData(false, true, nameof(ControllerMode.Keyboard))]
    [InlineData(true, false, nameof(ControllerMode.Suspended))]
    [InlineData(true, true, nameof(ControllerMode.Suspended))]
    public void ComputeMode_FullscreenSuspensionWinsOverKeyboardMode(bool blockedFullscreen, bool keyboardMode, string expected)
    {
        Assert.Equal(expected, ControllerLights.ComputeMode(blockedFullscreen, keyboardMode).ToString());
    }

    [Fact]
    public void ColorFor_EveryModeHasItsOwnColour()
    {
        var colors = Enum.GetValues<ControllerMode>().Select(ControllerLights.ColorFor).ToList();

        Assert.Equal(colors.Count, colors.Distinct().Count());
    }

    [Fact]
    public void ColorFor_EveryModeColourIsFullySaturated_SoTheLightbarDoesNotWashItOutToWhite()
    {
        foreach (var mode in Enum.GetValues<ControllerMode>())
        {
            var c = ControllerLights.ColorFor(mode);
            Assert.True(Math.Min(c.R, Math.Min(c.G, c.B)) == 0, $"{mode} colour {c} has no channel off");
        }
    }

    [Theory]
    [InlineData(nameof(ControllerMode.Mouse), 0xFF, 0x60, 0x00)]
    [InlineData(nameof(ControllerMode.Keyboard), 0x00, 0xFF, 0x40)]
    [InlineData(nameof(ControllerMode.Suspended), 0x00, 0x00, 0x40)]
    public void ColorFor_KeepsEachModesHueButNoBrighterThanSdlsOwnColours(string mode, int r, int g, int b)
    {
        var c = ControllerLights.ColorFor(Enum.Parse<ControllerMode>(mode));

        Assert.Equal(0x40, Math.Max(c.R, Math.Max(c.G, c.B)));
        Assert.Equal(Color.FromArgb(r, g, b).GetHue(), c.GetHue(), 1.5f);
    }

    [Fact]
    public void ScaleToPeak_ScalesEveryChannelByTheSameFactor()
    {
        var c = ControllerLights.ScaleToPeak(Color.FromArgb(0xC0, 0x60, 0x30), 0x40);

        Assert.Equal(Color.FromArgb(0x40, 0x20, 0x10).ToArgb(), c.ToArgb());
    }

    [Theory]
    [InlineData(nameof(BatteryLevel.Unknown), 0)]
    [InlineData(nameof(BatteryLevel.Empty), 1)]
    [InlineData(nameof(BatteryLevel.Low), 2)]
    [InlineData(nameof(BatteryLevel.Medium), 3)]
    [InlineData(nameof(BatteryLevel.Full), 5)]
    [InlineData(nameof(BatteryLevel.Wired), 5)]
    public void PlayerLightsFor_MoreChargeLightsMoreOfTheFiveLeds(string level, int expectedLitLeds)
    {
        byte mask = ControllerLights.PlayerLightsFor(Enum.Parse<BatteryLevel>(level));

        Assert.Equal(expectedLitLeds, BitOperations.PopCount(mask));
        Assert.Equal(0, mask & ~0x1F);
    }
}
