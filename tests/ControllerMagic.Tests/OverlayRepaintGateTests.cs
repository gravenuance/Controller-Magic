using ControllerMagic;
using Xunit;

namespace ControllerMagic.Tests;

public class OverlayRepaintGateTests
{
    private static readonly OverlayFrame Idle = new(KeyboardMode: true, Layer: 0, Sector: 2, Slot: 1);

    [Fact]
    public void NeedsRepaint_BeforeAnyPaint_IsTrue()
    {
        Assert.True(new OverlayRepaintGate().NeedsRepaint(Idle));
    }

    [Fact]
    public void NeedsRepaint_StateUnchangedAcrossASecondOfTicks_RepaintsOnlyOnce()
    {
        var gate = new OverlayRepaintGate();
        int repaints = 0;

        for (int tick = 0; tick < 60; tick++)
        {
            if (!gate.NeedsRepaint(Idle))
                continue;
            repaints++;
            gate.Painted(Idle);
        }

        Assert.Equal(1, repaints);
    }

    [Theory]
    [InlineData(false, 0, 2, 1)]
    [InlineData(true, 1, 2, 1)]
    [InlineData(true, 0, 3, 1)]
    [InlineData(true, 0, 2, 0)]
    public void NeedsRepaint_AnyDisplayedFieldChanges_IsTrue(bool keyboardMode, int layer, int sector, int slot)
    {
        var gate = new OverlayRepaintGate();
        gate.Painted(Idle);

        Assert.True(gate.NeedsRepaint(new OverlayFrame(keyboardMode, layer, sector, slot)));
    }

    [Fact]
    public void TileLabels_MatchTheLayoutsDisplayCharacters()
    {
        var layout = ControllerPoller.KeyboardLayout;

        for (int layer = 0; layer < layout.GetLength(0); layer++)
        for (int sector = 0; sector < layout.GetLength(1); sector++)
        for (int index = 0; index < layout.GetLength(2); index++)
            Assert.Equal(layout[layer, sector, index].Display.ToString(), KeyboardOverlayForm.TileLabels[layer, sector, index]);
    }
}
