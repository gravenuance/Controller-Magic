namespace ControllerMagic;

[Flags]
internal enum PadButtons
{
    None = 0,
    A = 1 << 0,
    B = 1 << 1,
    X = 1 << 2,
    Y = 1 << 3,
    LeftShoulder = 1 << 4,
    RightShoulder = 1 << 5,
    Back = 1 << 6,
    Start = 1 << 7,
    LeftThumb = 1 << 8,
    RightThumb = 1 << 9,
    DPadUp = 1 << 10,
    DPadDown = 1 << 11,
    DPadLeft = 1 << 12,
    DPadRight = 1 << 13,
    TouchpadClick = 1 << 14
}