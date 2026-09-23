namespace ControllerMagic;

internal struct PadState
{
    public bool IsConnected;

    public short LeftThumbX;
    public short LeftThumbY;
    public short RightThumbX;
    public short RightThumbY;

    public byte LeftTrigger;
    public byte RightTrigger;

    public PadButtons Buttons;

    // First finger on the touchpad, 0..1 from the top-left corner; only pads with a touchpad set these.
    public bool TouchActive;
    public float TouchX;
    public float TouchY;
}