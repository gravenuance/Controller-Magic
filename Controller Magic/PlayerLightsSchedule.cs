namespace ControllerMagic;

internal enum PlayerLightsSend
{
    None,
    Changed,
    Refresh,
}

// SDL blanks a DualSense's player LEDs once its sensor clock passes ~10.2 s, so the pattern is
// re-sent until one send lands past that; after it, each write costs the pad radio time for nothing.
internal sealed class PlayerLightsSchedule
{
    private static readonly TimeSpan RefreshEvery = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan RefreshFor = TimeSpan.FromSeconds(15);

    private readonly TimeProvider _clock;
    private byte? _applied;
    private long _openedAt;
    private long _sentAt;
    private bool _refreshing;

    public PlayerLightsSchedule(TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        _clock = clock;
    }

    // Call when a controller is opened, including a reopen after it dropped out.
    public void Restart()
    {
        _applied = null;
        _openedAt = _clock.GetTimestamp();
        _refreshing = true;
    }

    public PlayerLightsSend Decide(byte mask)
    {
        bool changed = _applied != mask;
        if (!changed && !RefreshDue())
            return PlayerLightsSend.None;

        _applied = mask;
        _sentAt = _clock.GetTimestamp();
        _refreshing = _clock.GetElapsedTime(_openedAt) < RefreshFor;
        return changed ? PlayerLightsSend.Changed : PlayerLightsSend.Refresh;
    }

    private bool RefreshDue() => _refreshing && _clock.GetElapsedTime(_sentAt) >= RefreshEvery;
}
