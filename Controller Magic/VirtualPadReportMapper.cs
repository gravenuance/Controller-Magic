using Nefarius.ViGEm.Client.Targets.Xbox360;

namespace ControllerMagic;

// Pure mapping from this app's own PadButtons flags to the ViGEm client's per-button
// Xbox360Button constants - kept separate from VigemBridge so it's testable with no driver,
// no ViGEmClient, and no fake/mock involved at all.
//
// There is deliberately no entry for Xbox360Button.Guide: PadButtons has no Guide bit to map
// from, so a virtual pad fed exclusively through this table can never report Guide as pressed.
// That omission - not a filter or an explicit "skip Guide" check - is the entire suppression
// mechanism this feature relies on.
internal static class VirtualPadReportMapper
{
    private static readonly (PadButtons Flag, Xbox360Button Button)[] CoreButtonMap =
    {
        (PadButtons.A, Xbox360Button.A),
        (PadButtons.B, Xbox360Button.B),
        (PadButtons.X, Xbox360Button.X),
        (PadButtons.Y, Xbox360Button.Y),
        (PadButtons.LeftShoulder, Xbox360Button.LeftShoulder),
        (PadButtons.RightShoulder, Xbox360Button.RightShoulder),
        (PadButtons.Back, Xbox360Button.Back),
        (PadButtons.Start, Xbox360Button.Start),
        (PadButtons.LeftThumb, Xbox360Button.LeftThumb),
        (PadButtons.RightThumb, Xbox360Button.RightThumb),
    };

    private static readonly (PadButtons Flag, Xbox360Button Button)[] DpadButtonMap =
    {
        (PadButtons.DPadUp, Xbox360Button.Up),
        (PadButtons.DPadDown, Xbox360Button.Down),
        (PadButtons.DPadLeft, Xbox360Button.Left),
        (PadButtons.DPadRight, Xbox360Button.Right),
    };

    // includeDpad=false is used while this app's own stick-as-mouse control is active: Windows'
    // built-in "XY focus navigation" (UWP/WinUI apps, and Shell surfaces built on that stack)
    // watches the D-pad and left thumbstick on any connected gamepad - real or virtual - to move
    // UI focus, which fights with using the stick as a mouse. Reporting the D-pad as never
    // pressed removes it from that feature's view entirely; VigemBridge.SubmitReport does the
    // equivalent for the left stick's axis values.
    public static IReadOnlyList<(Xbox360Button Button, bool Pressed)> MapButtons(PadButtons buttons, bool includeDpad = true)
    {
        var result = new (Xbox360Button, bool)[CoreButtonMap.Length + DpadButtonMap.Length];
        int i = 0;

        foreach (var (flag, button) in CoreButtonMap)
            result[i++] = (button, (buttons & flag) != 0);

        foreach (var (flag, button) in DpadButtonMap)
            result[i++] = (button, includeDpad && (buttons & flag) != 0);

        return result;
    }
}
