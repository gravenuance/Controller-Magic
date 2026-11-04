namespace ControllerMagic;

// SDL keeps a pad's last state when its reports stop arriving, so a Bluetooth pad drifting out of
// range would otherwise leave a stick deflected or a button held until the link recovers.
internal sealed class ReportWatchdog
{
    private readonly TimeProvider _clock;
    private readonly TimeSpan _staleAfter;
    private ulong? _lastStamp;
    private long _stampChangedAt;

    public ReportWatchdog(TimeProvider clock, TimeSpan staleAfter)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(staleAfter, TimeSpan.Zero);
        _clock = clock;
        _staleAfter = staleAfter;
    }

    // Set once input changed under a frozen stamp: the pad sends reports without it (a game can
    // switch a DualSense to simple reports), so judging by it would hold a live pad neutral.
    public bool IgnoresStamps { get; private set; }

    // A null stamp means the pad has no per-report counter, so it can never be judged stale.
    // inputChanged: the pad's state differs from the last read, which only a new report can do.
    public bool IsStale(ulong? stamp, bool inputChanged = false)
    {
        if (IgnoresStamps || stamp is not { } current)
            return false;

        if (current != _lastStamp)
        {
            _lastStamp = current;
            _stampChangedAt = _clock.GetTimestamp();
            return false;
        }

        if (_clock.GetElapsedTime(_stampChangedAt) < _staleAfter)
            return false;

        IgnoresStamps = inputChanged;
        return !inputChanged;
    }

    public void Reset()
    {
        _lastStamp = null;
        IgnoresStamps = false;
    }
}
