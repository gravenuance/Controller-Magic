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
    // SDL's own player colours peak here; full brightness only drains the battery faster.
    private const byte LightbarPeak = 0x40;

    // One channel off on purpose, or the lightbar washes pale UI colours out to near-white.
    private static readonly Color MouseColor = ScaleToPeak(Color.FromArgb(0xFF, 0x60, 0x00), LightbarPeak);
    private static readonly Color KeyboardColor = ScaleToPeak(Color.FromArgb(0x00, 0xFF, 0x40), LightbarPeak);
    private static readonly Color SuspendedColor = Color.FromArgb(0x00, 0x00, LightbarPeak);

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

    // Rounds to nearest so a dimmed channel keeps its share of the hue.
    internal static Color ScaleToPeak(Color hue, byte peak)
    {
        int brightest = Math.Max(hue.R, Math.Max(hue.G, hue.B));
        if (brightest == 0)
            return hue;

        byte Scale(byte channel) => (byte)((channel * peak + brightest / 2) / brightest);
        return Color.FromArgb(Scale(hue.R), Scale(hue.G), Scale(hue.B));
    }

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
