namespace ControllerMagic;

internal enum ControllerMode
{
    Mouse,
    Keyboard,
    Suspended,
}

internal enum BatteryLevel
{
    Unknown,
    Empty,
    Low,
    Medium,
    Full,
    Wired,
}

// The lightbar shows the mode; a DualSense's five player LEDs under the touchpad show the battery.
internal static class ControllerLights
{
    // Fully saturated on purpose: the lightbar washes the app's pale UI colours out to near-white.
    private static readonly Color MouseColor = Color.FromArgb(0xFF, 0x60, 0x00);
    private static readonly Color KeyboardColor = Color.FromArgb(0x00, 0xFF, 0x40);
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
        ControllerMode.Mouse => MouseColor,
        ControllerMode.Keyboard => KeyboardColor,
        ControllerMode.Suspended => SuspendedColor,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
    };

    // Bitmask over the five LEDs, centred like the console's own player-number patterns. Wired
    // shows full because SDL reports no charge level over USB.
    public static byte PlayerLightsFor(BatteryLevel level) => level switch
    {
        BatteryLevel.Unknown => 0x00,
        BatteryLevel.Empty => 0x04,
        BatteryLevel.Low => 0x0A,
        BatteryLevel.Medium => 0x15,
        BatteryLevel.Full or BatteryLevel.Wired => 0x1F,
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, null),
    };
}
