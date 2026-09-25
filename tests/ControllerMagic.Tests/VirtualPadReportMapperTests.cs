using ControllerMagic;
using Nefarius.ViGEm.Client.Targets.Xbox360;
using Xunit;

namespace ControllerMagic.Tests;

public class VirtualPadReportMapperTests
{
    private static readonly Xbox360Button[] AllButtons =
    [
        Xbox360Button.A, Xbox360Button.B, Xbox360Button.X, Xbox360Button.Y,
        Xbox360Button.LeftShoulder, Xbox360Button.RightShoulder, Xbox360Button.Back, Xbox360Button.Start,
        Xbox360Button.LeftThumb, Xbox360Button.RightThumb, Xbox360Button.Guide,
        Xbox360Button.Up, Xbox360Button.Down, Xbox360Button.Left, Xbox360Button.Right,
    ];

    private static string[] PressedNames(ushort mask) =>
        AllButtons.Where(b => (mask & b.Value) != 0).Select(b => b.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();

    [Fact]
    public void MapButtons_NoButtonsPressed_NothingIsPressed()
    {
        Assert.Equal(0, VirtualPadReportMapper.MapButtons(PadButtons.None));
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
        ushort mask = VirtualPadReportMapper.MapButtons((PadButtons)flagValue);

        Assert.Equal([expectedButtonName], PressedNames(mask));
    }

    [Fact]
    public void MapButtons_MultipleFlagsSet_AllCorrespondingButtonsArePressed()
    {
        ushort mask = VirtualPadReportMapper.MapButtons(PadButtons.A | PadButtons.DPadUp | PadButtons.Start);

        Assert.Equal(["A", "Start", "Up"], PressedNames(mask));
    }

    [Fact]
    public void MapButtons_NeverPressesGuide()
    {
        // PadButtons has no Guide bit at all, so no combination of flags can ever reach one -
        // this documents that the omission (not a runtime filter) is what suppresses Guide.
        ushort mask = VirtualPadReportMapper.MapButtons((PadButtons)(-1));

        Assert.Equal(0, mask & Xbox360Button.Guide.Value);
    }

    [Fact]
    public void MapButtons_IncludeDpadFalse_DpadButtonsStayUnpressedEvenWhenFlagsAreSet()
    {
        var allDpad = PadButtons.DPadUp | PadButtons.DPadDown | PadButtons.DPadLeft | PadButtons.DPadRight;

        Assert.Equal(0, VirtualPadReportMapper.MapButtons(allDpad, includeDpad: false));
    }

    [Fact]
    public void MapButtons_IncludeDpadFalse_NonDpadButtonsAreUnaffected()
    {
        ushort mask = VirtualPadReportMapper.MapButtons(PadButtons.A | PadButtons.Start, includeDpad: false);

        Assert.Equal(["A", "Start"], PressedNames(mask));
    }
}
