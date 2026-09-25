using Nefarius.ViGEm.Client.Targets.Xbox360;

namespace ControllerMagic;

// Pure mapping from this app's own PadButtons flags to the Xbox 360 report's wButtons bit mask -
// kept separate from VigemBridge so it's testable with no driver, no ViGEmClient, and no
// fake/mock involved at all.
//
// There is deliberately no entry for Xbox360Button.Guide: PadButtons has no Guide bit to map
// from, so a virtual pad fed exclusively through this table can never report Guide as pressed.
// That omission - not a filter or an explicit "skip Guide" check - is the entire suppression
// mechanism this feature relies on.
internal static class VirtualPadReportMapper
{
    private static readonly (PadButtons Flag, ushort Bit)[] CoreButtonMap =
    [
        (PadButtons.A, Xbox360Button.A.Value),
        (PadButtons.B, Xbox360Button.B.Value),
        (PadButtons.X, Xbox360Button.X.Value),
        (PadButtons.Y, Xbox360Button.Y.Value),
        (PadButtons.LeftShoulder, Xbox360Button.LeftShoulder.Value),
        (PadButtons.RightShoulder, Xbox360Button.RightShoulder.Value),
        (PadButtons.Back, Xbox360Button.Back.Value),
        (PadButtons.Start, Xbox360Button.Start.Value),
        (PadButtons.LeftThumb, Xbox360Button.LeftThumb.Value),
        (PadButtons.RightThumb, Xbox360Button.RightThumb.Value),
    ];

    private static readonly (PadButtons Flag, ushort Bit)[] DpadButtonMap =
    [
        (PadButtons.DPadUp, Xbox360Button.Up.Value),
        (PadButtons.DPadDown, Xbox360Button.Down.Value),
        (PadButtons.DPadLeft, Xbox360Button.Left.Value),
        (PadButtons.DPadRight, Xbox360Button.Right.Value),
    ];

    // includeDpad=false is used while this app's own stick-as-mouse control is active: Windows'
    // built-in "XY focus navigation" (UWP/WinUI apps, and Shell surfaces built on that stack)
    // watches the D-pad and left thumbstick on any connected gamepad - real or virtual - to move
    // UI focus, which fights with using the stick as a mouse. Reporting the D-pad as never
    // pressed removes it from that feature's view entirely; VigemBridge.SubmitReport does the
    // equivalent for the left stick's axis values.
    public static ushort MapButtons(PadButtons buttons, bool includeDpad = true)
    {
        ushort mask = Collect(CoreButtonMap, buttons);
        if (includeDpad)
            mask |= Collect(DpadButtonMap, buttons);
        return mask;
    }

    private static ushort Collect((PadButtons Flag, ushort Bit)[] map, PadButtons buttons)
    {
        ushort mask = 0;
        foreach (var (flag, bit) in map)
        {
            if ((buttons & flag) != 0)
                mask |= bit;
        }

        return mask;
    }
}
