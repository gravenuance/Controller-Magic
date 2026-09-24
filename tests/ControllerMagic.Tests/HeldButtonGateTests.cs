using ControllerMagic;
using Xunit;

namespace ControllerMagic.Tests;

public class HeldButtonGateTests
{
    [Fact]
    public void Filter_NothingSuppressed_PassesButtonsThrough()
    {
        var gate = new HeldButtonGate();

        Assert.Equal(PadButtons.A | PadButtons.B, gate.Filter(PadButtons.A | PadButtons.B));
    }

    [Fact]
    public void Filter_ButtonHeldAcrossSuppress_StaysHiddenUntilReleasedThenWorksAgain()
    {
        var gate = new HeldButtonGate();
        gate.SuppressHeld();

        Assert.Equal(PadButtons.None, gate.Filter(PadButtons.B));
        Assert.Equal(PadButtons.None, gate.Filter(PadButtons.B));
        Assert.Equal(PadButtons.None, gate.Filter(PadButtons.None));
        Assert.Equal(PadButtons.B, gate.Filter(PadButtons.B));
    }

    [Fact]
    public void Filter_ButtonPressedAfterResume_PassesWhileAHeldOverOneStaysHidden()
    {
        var gate = new HeldButtonGate();
        gate.SuppressHeld();
        gate.Filter(PadButtons.A);

        Assert.Equal(PadButtons.Start, gate.Filter(PadButtons.A | PadButtons.Start));
    }
}
