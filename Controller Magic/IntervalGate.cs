namespace ControllerMagic;

// Lets work through at most once per interval, for a call made far more often than its result changes.
// Use from one thread.
internal sealed class IntervalGate(TimeProvider clock, TimeSpan interval)
{
    private long? _lastAt;

    // True on the first call, then once the interval has passed since the last true.
    public bool TryEnter()
    {
        if (_lastAt is { } last && clock.GetElapsedTime(last) < interval)
            return false;

        _lastAt = clock.GetTimestamp();
        return true;
    }
}
