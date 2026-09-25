namespace ControllerMagic;

// Error boundary for a window's fire-and-forget async work: a failure is logged and shown as a
// short status instead of reaching the crash dialog, and CancelAll stops everything when the
// window closes while leaving the runner usable if the same window is shown again.
internal sealed class UiOperationRunner : IDisposable
{
    internal const string FailureText = "Something went wrong - see log.";

    private readonly AppLog _log;
    private CancellationTokenSource _lifetime = new();

    internal UiOperationRunner(AppLog log) => _log = log;

    // Callers check this after each await before touching controls.
    internal CancellationToken Token => _lifetime.Token;

    internal async Task RunAsync(string operation, Func<CancellationToken, Task> body, Action<string> showFailure)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(showFailure);

        var ct = _lifetime.Token;
        try
        {
            await body(ct).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _log.Error($"Settings: {operation} failed", ex);
            if (!ct.IsCancellationRequested)
                showFailure(FailureText);
        }
    }

    internal void CancelAll()
    {
        var old = _lifetime;
        _lifetime = new CancellationTokenSource();
        old.Cancel();
        old.Dispose();
    }

    public void Dispose()
    {
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
