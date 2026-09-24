namespace ControllerMagic;

// Paces reconnecting a virtual pad that failed to connect or dropped: waits 1s, then doubles, and
// stops after MaxAttempts so a broken ViGEmBus isn't retried (and logged) forever. A connect only
// clears the count once the pad has stayed up, so one that drops straight away still hits the cap.
internal sealed class VirtualPadRetry(TimeProvider clock)
{
    internal const int MaxAttempts = 6;
    private static readonly TimeSpan FirstDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan StableFor = TimeSpan.FromSeconds(60);

    private readonly Lock _gate = new();
    private int _failures;
    private DateTimeOffset _nextAttemptUtc;
    private DateTimeOffset? _connectedAtUtc;

    public bool CanAttempt
    {
        get
        {
            lock (_gate)
                return _failures == 0 || (_failures < MaxAttempts && clock.GetUtcNow() >= _nextAttemptUtc);
        }
    }

    public void RecordConnected()
    {
        lock (_gate)
            _connectedAtUtc = clock.GetUtcNow();
    }

    // Returns how many attempts in a row have now failed.
    public int RecordFailure()
    {
        lock (_gate)
        {
            var now = clock.GetUtcNow();
            if (now - _connectedAtUtc >= StableFor)
                _failures = 0;
            _connectedAtUtc = null;

            _failures++;
            _nextAttemptUtc = now + FirstDelay * Math.Pow(2, _failures - 1);
            return _failures;
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _failures = 0;
            _connectedAtUtc = null;
        }
    }
}
