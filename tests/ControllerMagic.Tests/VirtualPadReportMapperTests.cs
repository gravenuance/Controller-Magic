using ControllerMagic;
using Nefarius.ViGEm.Client.Targets.Xbox360;
using Xunit;

namespace ControllerMagic.Tests;

public class VirtualPadReportMapperTests
{
    [Fact]
    public void MapButtons_NoButtonsPressed_EveryEntryIsUnpressed()
    {
        var mapped = VirtualPadReportMapper.MapButtons(PadButtons.None);

        Assert.All(mapped, entry => Assert.False(entry.Pressed));
    }

    // PadButtons is internal, and a public [Theory] method can't expose an internal type in its
    // signature (CS0051) even with InternalsVisibleTo - so InlineData carries the raw int value
    // instead, cast back to PadButtons inside the method body.
    [Theory]
    [InlineData((int)PadButtons.A, "A")]
    [InlineData((int)PadButtons.B, "B")]
    [InlineData((int)PadButtons.X, "X")]
    [InlineData((int)PadButtons.Y, "Y")]
    [InlineData((int)PadButtons.LeftShoulder, "LeftShoulder")]
    [InlineData((int)PadButtons.RightShoulder, "RightShoulder")]
    [InlineData((int)PadButtons.Back, "Back")]
    [InlineData((int)PadButtons.Start, "Start")]
    [InlineData((int)PadButtons.LeftThumb, "LeftThumb")]
    [InlineData((int)PadButtons.RightThumb, "RightThumb")]
    [InlineData((int)PadButtons.DPadUp, "Up")]
    [InlineData((int)PadButtons.DPadDown, "Down")]
    [InlineData((int)PadButtons.DPadLeft, "Left")]
    [InlineData((int)PadButtons.DPadRight, "Right")]
    public void MapButtons_SingleFlagSet_OnlyTheCorrespondingXbox360ButtonIsPressed(int flagValue, string expectedButtonName)
    {
        var mapped = VirtualPadReportMapper.MapButtons((PadButtons)flagValue);

        var pressed = mapped.Where(m => m.Pressed).ToList();
        Assert.Single(pressed);
        Assert.Equal(expectedButtonName, pressed[0].Button.ToString());
    }

    private static readonly string[] ExpectedMultiFlagButtonNames = { "A", "Start", "Up" };

    [Fact]
    public void MapButtons_MultipleFlagsSet_AllCorrespondingButtonsArePressed()
    {
        var mapped = VirtualPadReportMapper.MapButtons(PadButtons.A | PadButtons.DPadUp | PadButtons.Start);

        var pressedNames = mapped.Where(m => m.Pressed).Select(m => m.Button.ToString()).OrderBy(n => n);
        Assert.Equal(ExpectedMultiFlagButtonNames, pressedNames);
    }

    [Fact]
    public void MapButtons_NeverProducesAGuideEntry()
    {
        // PadButtons has no Guide bit at all, so no combination of flags can ever reach one -
        // this documents that the omission (not a runtime filter) is what suppresses Guide.
        var mapped = VirtualPadReportMapper.MapButtons((PadButtons)(-1));

        Assert.DoesNotContain(mapped, m => m.Button == Xbox360Button.Guide);
    }

    private static readonly Xbox360Button[] DpadButtons =
        { Xbox360Button.Up, Xbox360Button.Down, Xbox360Button.Left, Xbox360Button.Right };

    [Fact]
    public void MapButtons_IncludeDpadFalse_DpadButtonsStayUnpressedEvenWhenFlagsAreSet()
    {
        var allDpad = PadButtons.DPadUp | PadButtons.DPadDown | PadButtons.DPadLeft | PadButtons.DPadRight;

        var mapped = VirtualPadReportMapper.MapButtons(allDpad, includeDpad: false);

        Assert.All(mapped.Where(m => DpadButtons.Contains(m.Button)), entry => Assert.False(entry.Pressed));
    }

    private static readonly string[] ExpectedNonDpadButtonNames = { "A", "Start" };

    [Fact]
    public void MapButtons_IncludeDpadFalse_NonDpadButtonsAreUnaffected()
    {
        var mapped = VirtualPadReportMapper.MapButtons(PadButtons.A | PadButtons.Start, includeDpad: false);

        var pressedNames = mapped.Where(m => m.Pressed).Select(m => m.Button.ToString()).OrderBy(n => n);
        Assert.Equal(ExpectedNonDpadButtonNames, pressedNames);
    }
}
