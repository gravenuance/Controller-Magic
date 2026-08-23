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

    public static bool TryReadAny(out PadState pad)
    {
        if (TryRead(_lastSlot, out pad))
            return true;

        for (int i = 0; i < 4; i++)
        {
            if (i == _lastSlot)
                continue;

            if (TryRead(i, out pad))
            {
                _lastSlot = i;
                return true;
            }
        }

        return false;
    }

    public static bool TryRead(int userIndex, out PadState pad)
    {
        pad = default;

        if (!XInput.GetState((uint)userIndex, out var state))
            return false;

        var g = state.Gamepad;
        PadButtons buttons = PadButtons.None;

        if (g.Buttons.HasFlag(GamepadButtons.A)) buttons |= PadButtons.A;
        if (g.Buttons.HasFlag(GamepadButtons.B)) buttons |= PadButtons.B;
        if (g.Buttons.HasFlag(GamepadButtons.X)) buttons |= PadButtons.X;
        if (g.Buttons.HasFlag(GamepadButtons.Y)) buttons |= PadButtons.Y;
        if (g.Buttons.HasFlag(GamepadButtons.LeftShoulder)) buttons |= PadButtons.LeftShoulder;
        if (g.Buttons.HasFlag(GamepadButtons.RightShoulder)) buttons |= PadButtons.RightShoulder;
        if (g.Buttons.HasFlag(GamepadButtons.Back)) buttons |= PadButtons.Back;
        if (g.Buttons.HasFlag(GamepadButtons.Start)) buttons |= PadButtons.Start;
        if (g.Buttons.HasFlag(GamepadButtons.LeftThumb)) buttons |= PadButtons.LeftThumb;
        if (g.Buttons.HasFlag(GamepadButtons.RightThumb)) buttons |= PadButtons.RightThumb;
        if (g.Buttons.HasFlag(GamepadButtons.DPadUp)) buttons |= PadButtons.DPadUp;
        if (g.Buttons.HasFlag(GamepadButtons.DPadDown)) buttons |= PadButtons.DPadDown;
        if (g.Buttons.HasFlag(GamepadButtons.DPadLeft)) buttons |= PadButtons.DPadLeft;
        if (g.Buttons.HasFlag(GamepadButtons.DPadRight)) buttons |= PadButtons.DPadRight;

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
