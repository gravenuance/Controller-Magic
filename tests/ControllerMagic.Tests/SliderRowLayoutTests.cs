using System.Windows.Forms;
using ControllerMagic;
using Xunit;

namespace ControllerMagic.Tests;

public class SliderRowLayoutTests
{
    private static readonly string[] LongestNames = ["Acceleration curve", "Turn off when idle", "Touchpad speed", "Speed ramp-up"];

    [Fact]
    public void Compute_PlacesNameSliderValueAndPreviewLeftToRightWithoutOverlap()
    {
        var c = SliderRowLayout.Compute(contentLeft: 14, contentWidth: 460, labelWidth: 120, readoutWidth: 90, vizWidth: 64, gap: 10);

        Assert.Equal(14, c.LabelX);
        Assert.Equal(14 + 120 + 10, c.SliderX);
        Assert.Equal(c.ReadoutX - 10, c.SliderX + c.SliderWidth);
        Assert.Equal(c.VizX - 10, c.ReadoutX + c.ReadoutWidth);
        Assert.Equal(14 + 460, c.VizX + 64);
    }

    [Fact]
    public void Compute_NoRoomLeft_GivesTheSliderZeroWidthRatherThanNegative()
    {
        var c = SliderRowLayout.Compute(contentLeft: 0, contentWidth: 200, labelWidth: 150, readoutWidth: 90, vizWidth: 64, gap: 10);

        Assert.Equal(0, c.SliderWidth);
    }

    // The real fonts and the longest name and value today, in the 520 px window (460 px inside a card).
    [Fact]
    public void Compute_LongestCurrentTexts_LeaveAUsableSlider()
    {
        int label = LongestNames.Max(text => TextRenderer.MeasureText(text, Theme.UiFontBold).Width);
        int readout = TextRenderer.MeasureText("10000 / 32767", Theme.MonoFont).Width;

        var c = SliderRowLayout.Compute(14, 460, label, readout, vizWidth: 64, gap: 10);

        Assert.True(c.SliderWidth >= 120, $"slider only {c.SliderWidth} px (name {label} px, value {readout} px)");
    }
}
