namespace ControllerMagic;

internal enum ControllerMode
{
    Mouse,
    Keyboard,
    LowBattery,
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

// The lightbar shows the mode, or red on low battery; a DualSense's white player LEDs stay off.
internal static class ControllerLights
{
    // SDL's own player colours peak here; full brightness only drains the battery faster.
    private const byte LightbarPeak = 0x40;

    // One channel off on purpose, or the lightbar washes pale UI colours out to near-white.
    private static readonly Color MouseColor = ScaleToPeak(Color.FromArgb(0xFF, 0x60, 0x00), LightbarPeak);
    private static readonly Color KeyboardColor = ScaleToPeak(Color.FromArgb(0x00, 0xFF, 0x40), LightbarPeak);
    private static readonly Color LowBatteryColor = Color.FromArgb(LightbarPeak, 0x00, 0x00);

    // Keyboard mode keeps its green on low battery, so the mode is never in doubt while typing.
    public static ControllerMode ComputeMode(bool blockedFullscreen, bool keyboardMode, BatteryLevel battery)
    {
        if (blockedFullscreen)
            return ControllerMode.Suspended;
        if (keyboardMode)
            return ControllerMode.Keyboard;
        return battery is BatteryLevel.Low or BatteryLevel.Empty ? ControllerMode.LowBattery : ControllerMode.Mouse;
    }

    // Null while a fullscreen game has the pad: its own colour shows instead.
    public static Color? LightbarFor(ControllerMode mode) => mode switch
    {
        ControllerMode.Mouse => MouseColor,
        ControllerMode.Keyboard => KeyboardColor,
        ControllerMode.LowBattery => LowBatteryColor,
        ControllerMode.Suspended => null,
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

    // Bitmask over the five LEDs; sent anyway, since SDL lights them in its reset when a pad connects.
    public const byte PlayerLightsOff = 0x00;
}
