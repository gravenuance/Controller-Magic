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

    private const float Centre = 250f;

    [Fact]
    public void DirtyAreas_NothingPaintedYet_IsTheWholeWindow()
    {
        Assert.Null(new OverlayRepaintGate().DirtyAreas(Idle, Centre, Centre));
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 1)]
    public void DirtyAreas_ModeOrLayerChanged_IsTheWholeWindow(bool keyboardMode, int layer)
    {
        var gate = new OverlayRepaintGate();
        gate.Painted(Idle);

        Assert.Null(gate.DirtyAreas(new OverlayFrame(keyboardMode, layer, Idle.Sector, Idle.Slot), Centre, Centre));
    }

    [Fact]
    public void DirtyAreas_SectorMoves_CoversOnlyTheOldAndNewTile()
    {
        var gate = new OverlayRepaintGate();
        gate.Painted(new OverlayFrame(true, 0, Sector: 0, Slot: 0));

        var areas = gate.DirtyAreas(new OverlayFrame(true, 0, Sector: 2, Slot: 0), Centre, Centre);

        Assert.NotNull(areas);
        Assert.Equal(2, areas.Count);
        // Sector 0 is straight up and sector 2 straight left, both in the innermost ring (140 px out).
        Assert.Contains(areas, a => a.Contains(250, 110) && a.Width < 60 && a.Height < 60);
        Assert.Contains(areas, a => a.Contains(110, 250) && a.Width < 60 && a.Height < 60);
    }

    [Fact]
    public void DirtyAreas_SlotMovesWithinASector_CoversBothRings()
    {
        var gate = new OverlayRepaintGate();
        gate.Painted(new OverlayFrame(true, 0, Sector: 0, Slot: 0));

        var areas = gate.DirtyAreas(new OverlayFrame(true, 0, Sector: 0, Slot: 1), Centre, Centre);

        Assert.NotNull(areas);
        Assert.Contains(areas, a => a.Contains(250, 110));
        // The second ring sits 44 px further out.
        Assert.Contains(areas, a => a.Contains(250, 66));
    }

    [Fact]
    public void DirtyAreas_HighlightMovesToTheCentre_CoversOnlyTheTileThatLostIt()
    {
        var gate = new OverlayRepaintGate();
        gate.Painted(new OverlayFrame(true, 0, Sector: 0, Slot: 0));

        var areas = gate.DirtyAreas(new OverlayFrame(true, 0, Sector: -1, Slot: 0), Centre, Centre);

        Assert.NotNull(areas);
        var area = Assert.Single(areas);
        Assert.True(area.Contains(250, 110));
    }

    [Fact]
    public void DirtyAreas_NoHighlightBeforeOrAfter_IsEmpty()
    {
        var gate = new OverlayRepaintGate();
        gate.Painted(new OverlayFrame(true, 0, Sector: -1, Slot: 0));

        var areas = gate.DirtyAreas(new OverlayFrame(true, 0, Sector: -1, Slot: 2), Centre, Centre);

        Assert.NotNull(areas);
        Assert.Empty(areas);
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
