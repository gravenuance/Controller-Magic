namespace ControllerMagic;

// Fires once when a control is pressed, then again at a steady rate while it stays held, like a
// keyboard key's auto-repeat. Use from one thread.
internal sealed class HoldRepeat(TimeProvider clock, TimeSpan delay, TimeSpan interval)
{
    private long? _lastFiredAt;
    private bool _repeating;

    public bool Fires(bool held)
    {
        if (!held)
        {
            Reset();
            return false;
        }

        if (_lastFiredAt is { } last && clock.GetElapsedTime(last) < (_repeating ? interval : delay))
            return false;

        _repeating = _lastFiredAt is not null;
        _lastFiredAt = clock.GetTimestamp();
        return true;
    }

    public void Reset()
    {
        _lastFiredAt = null;
        _repeating = false;
    }
}
