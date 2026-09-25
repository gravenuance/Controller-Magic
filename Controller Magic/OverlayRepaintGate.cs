namespace ControllerMagic;

// Everything the keyboard overlay's picture depends on.
internal readonly record struct OverlayFrame(bool KeyboardMode, int Layer, int Sector, int Slot)
{
    public static OverlayFrame Of(ControllerPoller poller) =>
        new(poller.KeyboardMode, poller.KeyboardLayer, poller.CurrentSector, poller.SlotIndex);
}

// Repainting the 500x500 layered overlay is costly, so the frame timer only asks for one when
// the displayed state differs from what was last painted.
internal sealed class OverlayRepaintGate
{
    private OverlayFrame? _painted;

    public bool NeedsRepaint(OverlayFrame current) => _painted != current;

    public void Painted(OverlayFrame frame) => _painted = frame;
}
