namespace ControllerMagic;

// Remembers when a slow check last completed, so showing a window again soon after can skip it.
// Unlike IntervalGate, a check that was cancelled or failed never counts. Use from one thread.
internal sealed class RecentCheck(TimeProvider clock, TimeSpan freshFor)
{
    private long? _completedAt;

    public bool IsFresh => _completedAt is { } at && clock.GetElapsedTime(at) < freshFor;

    public void MarkCompleted() => _completedAt = clock.GetTimestamp();

    // For when something this app did may have changed the answer.
    public void Invalidate() => _completedAt = null;
}
