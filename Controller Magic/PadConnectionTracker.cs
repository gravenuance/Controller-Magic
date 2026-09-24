namespace ControllerMagic;

internal enum PadSource
{
    None,
    XInput,
    Sdl,
}

// Numbers each controller connection across both readers, so HidHide sees a reconnect, or a switch
// from one reader to the other, as a new connection to hide.
internal sealed class PadConnectionTracker
{
    private (PadSource Source, int XInputSlot, PhysicalDeviceIdentity? SdlIdentity, int SdlSerial)? _last;

    public int Serial { get; private set; }

    public PhysicalDeviceIdentity? Identity { get; private set; }

    public void Observe(PadSource source, int xinputSlot, PhysicalDeviceIdentity? sdlIdentity, int sdlSerial)
    {
        var key = source switch
        {
            PadSource.XInput => (source, xinputSlot, null, 0),
            PadSource.Sdl => (source, 0, sdlIdentity, sdlSerial),
            _ => (source, 0, (PhysicalDeviceIdentity?)null, 0),
        };
        if (key == _last)
            return;

        _last = key;
        Identity = source switch
        {
            // HidHideBridge resolves the XInput placeholder to every physical XInput pad.
            PadSource.XInput => PhysicalDeviceIdentity.ForXInputSlot(xinputSlot),
            PadSource.Sdl => sdlIdentity,
            _ => null,
        };
        if (source != PadSource.None)
            Serial++;
    }
}
