using Vortice.XInput;

namespace ControllerMagic;

// XInput reads Xbox-layout controllers regardless of window focus and regardless of wired vs.
// Bluetooth connection, since it rides the same Xbox peripheral driver stack the controller uses
// either way. Unlike Windows.Gaming.Input, it isn't gated by which app currently has focus, which
// matters for a background tray utility like this one.
internal static class XInputPadReader
{
    // XInput exposes 4 fixed slots; a controller can land in any of them depending on plug-in
    // order, so hardcoding slot 0 misses anything not lucky enough to claim it first. Sticking
    // with the last slot that worked avoids hopping between controllers if more than one is
    // connected, only rescanning once that slot actually goes quiet.
    private static int _lastSlot;

    // Slot the last successful TryReadAny() call landed on - only meaningful right after a call
    // that returned true. Exists so the UI can show which XInput slot the controller claimed.
    public static int LastSlot => _lastSlot;

    private const int SlotCount = 4;

    // excludedSlots is a bit per XInput user index that may hold this process's own ViGEm virtual
    // pad. XInput's public API exposes no device identity at all - just a slot number - so
    // without this, a virtual Xbox 360 controller this app creates for itself is
    // indistinguishable from a real one, and slot-scanning can end up reading back its own
    // (deliberately neutered) output as if it were fresh input instead of the real controller.
    public static bool TryReadAny(out PadState pad, int excludedSlots = 0)
    {
        pad = default;

        if (!IsExcluded(_lastSlot, excludedSlots) && TryRead(_lastSlot, out pad))
            return true;

        for (int i = 0; i < SlotCount; i++)
        {
            if (i == _lastSlot || IsExcluded(i, excludedSlots))
                continue;

            if (TryRead(i, out pad))
            {
                _lastSlot = i;
                return true;
            }
        }

        return false;
    }

    // A bit per user index with a controller in it right now.
    public static int ConnectedSlots()
    {
        int slots = 0;
        for (int i = 0; i < SlotCount; i++)
        {
            if (XInput.GetState((uint)i, out _))
                slots |= 1 << i;
        }

        return slots;
    }

    private static bool IsExcluded(int slot, int excludedSlots) => (excludedSlots & (1 << slot)) != 0;

    public static bool TryRead(int userIndex, out PadState pad)
    {
        pad = default;

        if (!XInput.GetState((uint)userIndex, out var state))
            return false;

        var g = state.Gamepad;
        PadButtons buttons = PadButtons.None;

        // Same boxing pitfall as ControllerPoller.WasPressed, but worse here: this runs
        // unconditionally every ~8ms tick whenever an XInput controller is connected, rather than
        // only for buttons that are actually pressed. Bitwise checks avoid it entirely.
        var gb = g.Buttons;
        if ((gb & GamepadButtons.A) != 0) buttons |= PadButtons.A;
        if ((gb & GamepadButtons.B) != 0) buttons |= PadButtons.B;
        if ((gb & GamepadButtons.X) != 0) buttons |= PadButtons.X;
        if ((gb & GamepadButtons.Y) != 0) buttons |= PadButtons.Y;
        if ((gb & GamepadButtons.LeftShoulder) != 0) buttons |= PadButtons.LeftShoulder;
        if ((gb & GamepadButtons.RightShoulder) != 0) buttons |= PadButtons.RightShoulder;
        if ((gb & GamepadButtons.Back) != 0) buttons |= PadButtons.Back;
        if ((gb & GamepadButtons.Start) != 0) buttons |= PadButtons.Start;
        if ((gb & GamepadButtons.LeftThumb) != 0) buttons |= PadButtons.LeftThumb;
        if ((gb & GamepadButtons.RightThumb) != 0) buttons |= PadButtons.RightThumb;
        if ((gb & GamepadButtons.DPadUp) != 0) buttons |= PadButtons.DPadUp;
        if ((gb & GamepadButtons.DPadDown) != 0) buttons |= PadButtons.DPadDown;
        if ((gb & GamepadButtons.DPadLeft) != 0) buttons |= PadButtons.DPadLeft;
        if ((gb & GamepadButtons.DPadRight) != 0) buttons |= PadButtons.DPadRight;

        pad = new PadState
        {
            IsConnected = true,
            LeftThumbX = g.LeftThumbX,
            LeftThumbY = g.LeftThumbY,
            RightThumbX = g.RightThumbX,
            RightThumbY = g.RightThumbY,
            LeftTrigger = g.LeftTrigger,
            RightTrigger = g.RightTrigger,
            Buttons = buttons
        };

        return true;
    }
}
