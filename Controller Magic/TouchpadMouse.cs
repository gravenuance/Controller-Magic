namespace ControllerMagic;

internal enum TouchClick
{
    None,
    Left,
    Right,
}

internal readonly record struct TouchpadMouseOutput(int Dx, int Dy, TouchClick Click, bool HoldLeft);

// Laptop-style touchpad: sliding moves the cursor, a tap left-clicks (two make a double-click),
// holding still right-clicks, and pressing the pad down holds the left button so a slide drags.
internal sealed class TouchpadMouse
{
    internal static readonly TimeSpan TapMaxDuration = TimeSpan.FromMilliseconds(250);
    internal static readonly TimeSpan LongPressDuration = TimeSpan.FromMilliseconds(500);

    // Travel, as a fraction of the pad's width, a touch may drift and still count as a tap or hold.
    private const float TapSlop = 0.02f;

    // The DualSense pad reports 1920x1080 units, so a normalised vertical step covers less ground.
    internal const float PadAspect = 1080f / 1920f;

    private readonly TimeProvider _clock;
    private bool _touching;
    private float _lastX;
    private float _lastY;
    private float _travel;
    private long _touchStartedAt;
    private bool _pressedDuringTouch;
    private bool _longPressFired;
    private double _dxRemainder;
    private double _dyRemainder;

    public TouchpadMouse(TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        _clock = clock;
    }

    // x and y are 0..1 from the pad's top-left corner.
    public TouchpadMouseOutput Update(bool touchActive, float x, float y, bool padPressed, float pixelsPerPadWidth)
    {
        if (!touchActive)
            return new TouchpadMouseOutput(0, 0, EndTouch(), padPressed);

        if (!_touching)
        {
            BeginTouch(x, y);
            _pressedDuringTouch = padPressed;
            return new TouchpadMouseOutput(0, 0, TouchClick.None, padPressed);
        }

        _pressedDuringTouch |= padPressed;
        float stepX = x - _lastX;
        float stepY = (y - _lastY) * PadAspect;
        _lastX = x;
        _lastY = y;
        _travel += MathF.Sqrt(stepX * stepX + stepY * stepY);

        var (dx, dy) = ToPixels(stepX, stepY, pixelsPerPadWidth);
        return new TouchpadMouseOutput(dx, dy, CheckLongPress(), padPressed);
    }

    // Forgets the current touch, e.g. when the touchpad switches to driving the on-screen keyboard.
    public void Reset()
    {
        _touching = false;
        _dxRemainder = 0;
        _dyRemainder = 0;
    }

    private void BeginTouch(float x, float y)
    {
        _touching = true;
        _lastX = x;
        _lastY = y;
        _travel = 0;
        _touchStartedAt = _clock.GetTimestamp();
        _longPressFired = false;
    }

    private bool IsStillGesture => !_pressedDuringTouch && !_longPressFired && _travel <= TapSlop;

    private TouchClick CheckLongPress()
    {
        if (!IsStillGesture || _clock.GetElapsedTime(_touchStartedAt) < LongPressDuration)
            return TouchClick.None;

        _longPressFired = true;
        return TouchClick.Right;
    }

    private TouchClick EndTouch()
    {
        if (!_touching)
            return TouchClick.None;

        bool isTap = IsStillGesture && _clock.GetElapsedTime(_touchStartedAt) <= TapMaxDuration;
        Reset();
        return isTap ? TouchClick.Left : TouchClick.None;
    }

    // Sub-pixel remainders carry over so a slow slide still moves smoothly instead of truncating to zero.
    private (int Dx, int Dy) ToPixels(float stepX, float stepY, float pixelsPerPadWidth)
    {
        double exactX = stepX * (double)pixelsPerPadWidth + _dxRemainder;
        double exactY = stepY * (double)pixelsPerPadWidth + _dyRemainder;
        int dx = (int)exactX;
        int dy = (int)exactY;
        _dxRemainder = exactX - dx;
        _dyRemainder = exactY - dy;
        return (dx, dy);
    }
}
