namespace ControllerMagic;

// Identifies a specific physical controller. InterfacePath is a Windows device *interface* path
// (what SDL_JoystickPath reports, e.g. "\\?\hid#vid_...&pid_...#...#{guid}") - HidHide's
// block-list actually keys on a device *instance* path instead (e.g. "HID\VID_...\..."), a
// related but different string; HidHideBridge converts between the two via
// Nefarius.Utilities.DeviceManagement before calling into HidHide. VendorId/ProductId are
// carried along only for logging and status text, since several identical controllers would
// share the same VID/PID but never the same interface path.
internal readonly record struct PhysicalDeviceIdentity(string InterfacePath, ushort VendorId, ushort ProductId)
{
    // SDL's XInput backend reports this literal instead of a real device path (SDL_xinputjoystick.c).
    private const string XInputPlaceholderPrefix = "XInput#";

    public bool IsValid => !string.IsNullOrEmpty(InterfacePath);

    public static PhysicalDeviceIdentity ForXInputSlot(int slot) => new($"{XInputPlaceholderPrefix}{slot}", 0, 0);

    public static bool IsXInputPlaceholderPath(string? path) =>
        path != null && path.StartsWith(XInputPlaceholderPrefix, StringComparison.Ordinal);
}
