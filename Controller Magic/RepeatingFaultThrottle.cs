namespace ControllerMagic;

// IsNew: log it in full. Otherwise Repeats is how often the same fault recurred since last logged.
internal readonly record struct FaultReport(bool IsNew, int Repeats);

// Decides when a fault is worth a log entry: a new one straight away, the same one again only as
// a count at most once a minute, so an exception thrown every tick can't flood the log.
internal sealed class RepeatingFaultThrottle(TimeProvider clock)
{
    private static readonly TimeSpan RepeatReportInterval = TimeSpan.FromMinutes(1);

    private string? _lastSignature;
    private int _repeats;
    private DateTimeOffset _lastReportUtc;

    public FaultReport? Record(Exception ex)
    {
        ArgumentNullException.ThrowIfNull(ex);

        var now = clock.GetUtcNow();
        string signature = $"{ex.GetType().FullName}|{ex.StackTrace}";
        if (signature != _lastSignature)
        {
            _lastSignature = signature;
            _repeats = 0;
            _lastReportUtc = now;
            return new FaultReport(IsNew: true, Repeats: 0);
        }

        _repeats++;
        if (now - _lastReportUtc < RepeatReportInterval)
            return null;

        var report = new FaultReport(IsNew: false, Repeats: _repeats);
        _repeats = 0;
        _lastReportUtc = now;
        return report;
    }
}
