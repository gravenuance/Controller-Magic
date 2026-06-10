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
}