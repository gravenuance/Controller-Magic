using ControllerMagic;
using Xunit;

namespace ControllerMagic.Tests;

// Layer 1 is the digits layer: sector 0 (up) holds "1" and "9", sector 2 (left) holds only "3".
public class TouchWheelTests
{
    private const int DigitsLayer = 1;

    [Fact]
    public void MapTouchToWheel_CentreOfThePad_SelectsNothing()
    {
        Assert.Null(ControllerPoller.MapTouchToWheel(0.5f, 0.5f, DigitsLayer));
    }

    [Fact]
    public void MapTouchToWheel_TopEdge_SelectsOutermostRingOfUpSector()
    {
        var selection = ControllerPoller.MapTouchToWheel(0.5f, 0.0f, DigitsLayer);

        Assert.Equal(new WheelSelection(Sector: 0, Ring: 1), selection);
    }

    [Fact]
    public void MapTouchToWheel_JustOutsideTheCentre_SelectsInnermostRing()
    {
        var selection = ControllerPoller.MapTouchToWheel(0.5f, 0.35f, DigitsLayer);

        Assert.Equal(new WheelSelection(Sector: 0, Ring: 0), selection);
    }

    [Fact]
    public void MapTouchToWheel_SectorWithOneKey_AnyDistanceSelectsIt()
    {
        var selection = ControllerPoller.MapTouchToWheel(0.0f, 0.5f, DigitsLayer);

        Assert.Equal(new WheelSelection(Sector: 2, Ring: 0), selection);
    }

    [Fact]
    public void MapTouchToWheel_RightEdge_SelectsRightSector()
    {
        Assert.Equal(6, ControllerPoller.MapTouchToWheel(1.0f, 0.5f, DigitsLayer)?.Sector);
    }

    [Fact]
    public void MapTouchToWheel_EveryPointOnThePad_IsNullOrAValidKey()
    {
        for (float x = 0; x <= 1.0f; x += 0.05f)
        {
            for (float y = 0; y <= 1.0f; y += 0.05f)
            {
                for (int layer = 0; layer < 3; layer++)
                {
                    var selection = ControllerPoller.MapTouchToWheel(x, y, layer);
                    if (selection is not { } s)
                        continue;

                    Assert.InRange(s.Sector, 0, 7);
                    Assert.NotEqual(0, ControllerPoller.KeyboardLayout[layer, s.Sector, s.Ring].Vk);
                }
            }
        }
    }

    [Fact]
    public void TouchDrivesWheel_StickCentredAndFingerOnALetter_TouchSelects()
    {
        Assert.True(ControllerPoller.TouchDrivesWheel(stickSector: -1, touch: new WheelSelection(0, 1)));
    }

    [Fact]
    public void TouchDrivesWheel_StickPushed_StickKeepsTheSelectionForAPadPress()
    {
        Assert.False(ControllerPoller.TouchDrivesWheel(stickSector: 3, touch: new WheelSelection(0, 1)));
    }

    [Fact]
    public void TouchDrivesWheel_FingerInTheCentre_LeavesItToTheStick()
    {
        Assert.False(ControllerPoller.TouchDrivesWheel(stickSector: -1, touch: null));
    }
}
