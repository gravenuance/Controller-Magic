using System.Runtime.InteropServices;

namespace ControllerMagic;

// Diagnostic-only: periodically logs this process's Windows USER/GDI object counts. Added after
// a 2026-09-13 crash (System.Windows.Forms.Timer.TimerNativeWindow.EnsureHandle failing on a
// tray-icon right-click, "used all of its system allowance of handles for Window Manager
// objects") that a full static review of every icon/form/timer/control construction site
// couldn't explain - every previously-known leak pattern in this codebase was already fixed, and
// the crashing stack trace never touched a line of this app's own code. A slow, silent handle
// leak only shows up as a count climbing over a session; this makes that visible in app.log
// instead of only being visible after something has already hit the ~10,000 per-process ceiling.
internal sealed class ResourceUsageMonitor : IDisposable
{
    private const uint GrGdiObjects = 0;
    private const uint GrUserObjects = 1;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetGuiResources(IntPtr hProcess, uint uiFlags);

    // A constant pseudo-handle: nothing to allocate or close, unlike Process.GetCurrentProcess().
    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    private static readonly IntPtr CurrentProcess = GetCurrentProcess();

    private readonly System.Threading.Timer _timer;

    public ResourceUsageMonitor(TimeSpan interval)
    {
        _timer = new System.Threading.Timer(_ => LogPeriodicIfMoved(), null, interval, interval);
    }

    // A periodic line is only worth writing when a count has moved this far since the last one: a
    // leak still shows as a steady run of lines, and a steady process stays out of the log.
    internal const uint PeriodicLogStep = 25;

    // Only the timer touches these, and its callbacks don't overlap at a 10-minute interval.
    private uint? _loggedGdi;
    private uint? _loggedUser;

    private void LogPeriodicIfMoved()
    {
        uint gdiObjects = GetGuiResources(CurrentProcess, GrGdiObjects);
        uint userObjects = GetGuiResources(CurrentProcess, GrUserObjects);
        if (!HasMoved(_loggedGdi, gdiObjects) && !HasMoved(_loggedUser, userObjects))
            return;

        _loggedGdi = gdiObjects;
        _loggedUser = userObjects;
        AppLog.Default.Info($"ResourceUsageMonitor (periodic): GDI objects={gdiObjects}, USER objects={userObjects}");
    }

    internal static bool HasMoved(uint? logged, uint current) =>
        logged is not { } last || (current > last ? current - last : last - current) >= PeriodicLogStep;

    // For GamepadPassthroughController's safety cutoff: a synchronous read of the live count,
    // separate from the logged snapshot below, so a caller can act on the value directly.
    public static uint GetUserObjectCount() => GetGuiResources(CurrentProcess, GrUserObjects);

    // Callable on demand around a specific action (a HidHide/ViGEm activate or deactivate, a
    // Settings dialog open/close), not just from the periodic timer - a count that jumps between
    // a "before" and "after" snapshot around one specific action pins the leak to that action
    // directly, instead of only being visible at the next scheduled snapshot with no way to tell
    // what happened in between.
    public static void LogSnapshot(string context)
    {
        try
        {
            uint gdiObjects = GetGuiResources(CurrentProcess, GrGdiObjects);
            uint userObjects = GetGuiResources(CurrentProcess, GrUserObjects);
            AppLog.Default.Info($"ResourceUsageMonitor ({context}): GDI objects={gdiObjects}, USER objects={userObjects}");
        }
        catch (Exception ex)
        {
            AppLog.Default.Warning("ResourceUsageMonitor: failed to query resource usage", ex);
        }
    }

    public void Dispose() => _timer.Dispose();
}
