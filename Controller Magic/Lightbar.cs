namespace ControllerMagic;

internal enum ControllerMode
{
    Mouse,
    Keyboard,
    Suspended,
}

// Shows the current mode on controllers with a lightbar (DualSense, DualShock 4).
internal static class Lightbar
{
    private static readonly Color SuspendedColor = Color.FromArgb(0x00, 0x00, 0x40);

    public static ControllerMode ComputeMode(bool blockedFullscreen, bool keyboardMode)
    {
        if (blockedFullscreen)
            return ControllerMode.Suspended;
        return keyboardMode ? ControllerMode.Keyboard : ControllerMode.Mouse;
    }

    // Suspended is a dim version of the PlayStation's own blue, so a game that sets no colour looks normal.
    public static Color ColorFor(ControllerMode mode) => mode switch
    {
        ControllerMode.Mouse => Theme.Accent,
        ControllerMode.Keyboard => Theme.Good,
        ControllerMode.Suspended => SuspendedColor,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
    };
}
