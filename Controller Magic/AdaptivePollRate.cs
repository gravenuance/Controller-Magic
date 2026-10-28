namespace ControllerMagic;

// Picks the poll interval: fast while the pad is in use, slow once it has sat untouched, so a
// pad left on the desk isn't read ~125 times a second. Pure: the caller passes the time in.
internal sealed class AdaptivePollRate(int activeIntervalMs, int restingIntervalMs, long restAfterMs)
{
    private long? _lastActiveMs;

    // Called every tick with a connected pad; any activity returns to the fast interval at once.
    public int Next(bool active, long nowMs)
    {
        if (active || _lastActiveMs is null)
        {
            _lastActiveMs = nowMs;
            return activeIntervalMs;
        }

        return nowMs - _lastActiveMs.Value >= restAfterMs ? restingIntervalMs : activeIntervalMs;
    }

    // The next pad starts fast, however long the last one sat idle.
    public void Reset() => _lastActiveMs = null;
}
