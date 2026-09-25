namespace ControllerMagic;

internal enum ExceptionNotice
{
    // A UI-thread exception WinForms caught; the app carries on.
    Survived,
    // A background-thread exception; the process is about to end.
    Crashed
}

// Last-chance handling for exceptions nothing else caught. Each notice is shown at most once per
// run, and the latch is set before showing it: a message box pumps messages, so a timer that keeps
// throwing would otherwise stack dialogs.
internal sealed class UnhandledExceptionReporter(AppLog log, TimeProvider clock, Action<ExceptionNotice> notify)
{
    private readonly RepeatingFaultThrottle _survivableFaults = new(clock);
    private readonly Lock _throttleLock = new();
    private int _survivedNoticeShown;
    private int _crashNoticeShown;

    public void ReportSurvivable(Exception ex)
    {
        ArgumentNullException.ThrowIfNull(ex);

        FaultReport? report;
        lock (_throttleLock)
            report = _survivableFaults.Record(ex);

        if (report is { IsNew: true })
            log.Error("Unhandled UI exception; continuing", ex);
        else if (report is { } repeat)
            log.Error($"Unhandled UI exception repeated {repeat.Repeats} more times");

        NotifyOnce(ref _survivedNoticeShown, ExceptionNotice.Survived);
    }

    public void ReportFatal(Exception? ex)
    {
        log.Error("Unhandled exception", ex);
        NotifyOnce(ref _crashNoticeShown, ExceptionNotice.Crashed);
    }

    private void NotifyOnce(ref int shown, ExceptionNotice notice)
    {
        if (Interlocked.Exchange(ref shown, 1) == 0)
            notify(notice);
    }
}
