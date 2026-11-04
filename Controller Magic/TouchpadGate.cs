namespace ControllerMagic;

// A thumb or palm resting on the touchpad while the sticks or buttons are in use must not move the
// cursor or click: the pad is ignored during other input, for a short quiet time after it, and for
// the rest of any touch that was already down when it was blocked.
internal sealed class TouchpadGate(TimeProvider clock, TimeSpan quietTime)
{
    internal static readonly TimeSpan DefaultQuietTime = TimeSpan.FromMilliseconds(300);

    private long? _otherInputAt;
    private bool _touchBlocked;

    public bool Allows(bool touchActive, bool otherInputActive)
    {
        if (otherInputActive)
        {
            _otherInputAt = clock.GetTimestamp();
            _touchBlocked |= touchActive;
            return false;
        }

        if (!touchActive)
        {
            _touchBlocked = false;
            return true;
        }

        if (_otherInputAt is { } at && clock.GetElapsedTime(at) < quietTime)
            _touchBlocked = true;

        return !_touchBlocked;
    }
}
