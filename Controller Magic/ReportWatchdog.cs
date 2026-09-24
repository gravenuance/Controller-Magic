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

    // A null stamp means the pad has no per-report counter, so it can never be judged stale.
    public bool IsStale(ulong? stamp)
    {
        if (stamp is not { } current)
            return false;

        if (current != _lastStamp)
        {
            _lastStamp = current;
            _stampChangedAt = _clock.GetTimestamp();
            return false;
        }

        return _clock.GetElapsedTime(_stampChangedAt) >= _staleAfter;
    }

    public void Reset() => _lastStamp = null;
}
