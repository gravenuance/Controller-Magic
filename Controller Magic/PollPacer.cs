using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace ControllerMagic;

// Waits between poll ticks. A high-resolution waitable timer gives ~8 ms waits (measured 8.4-8.5 ms)
// without raising the system-wide timer resolution, which Windows 11 may also ignore for a process
// with no visible window. Without one (before Windows 10 1803), Sleep at a 1 ms resolution that is
// held only for short waits. Use from one thread.
internal sealed class PollPacer : IDisposable
{
    private const uint CreateWaitableTimerHighResolution = 0x2;
    private const uint TimerModifyStateAndSynchronize = 0x0002 | 0x00100000;
    private const uint Infinite = 0xFFFFFFFF;
    private const int CoarseWaitMs = 50;

    private readonly TimerResolutionLease _resolution = new();
    private SafeWaitHandle? _timer;

    public PollPacer()
    {
        var timer = CreateWaitableTimerExW(IntPtr.Zero, null, CreateWaitableTimerHighResolution, TimerModifyStateAndSynchronize);
        if (timer.IsInvalid)
        {
            AppLog.Default.Warning($"PollPacer: no high-resolution timer ({new Win32Exception(Marshal.GetLastPInvokeError()).Message}); pacing with Sleep");
            timer.Dispose();
            return;
        }

        _timer = timer;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeWaitHandle CreateWaitableTimerExW(IntPtr lpTimerAttributes, string? lpTimerName, uint dwFlags, uint dwDesiredAccess);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWaitableTimer(SafeWaitHandle hTimer, ref long lpDueTime, int lPeriod, IntPtr pfnCompletionRoutine, IntPtr lpArgToCompletionRoutine, [MarshalAs(UnmanagedType.Bool)] bool fResume);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(SafeWaitHandle hHandle, uint dwMilliseconds);

    public void Wait(int milliseconds)
    {
        if (_timer is { } timer && TryWaitOnTimer(timer, milliseconds))
            return;

        if (milliseconds < CoarseWaitMs)
            _resolution.Hold();
        else
            _resolution.Release();
        Thread.Sleep(milliseconds);
    }

    private bool TryWaitOnTimer(SafeWaitHandle timer, int milliseconds)
    {
        long dueTime = -milliseconds * 10_000L; // Relative, in 100 ns units.
        if (SetWaitableTimer(timer, ref dueTime, 0, IntPtr.Zero, IntPtr.Zero, fResume: false)
            && WaitForSingleObject(timer, Infinite) == 0)
        {
            return true;
        }

        AppLog.Default.Warning($"PollPacer: high-resolution timer failed ({new Win32Exception(Marshal.GetLastPInvokeError()).Message}); pacing with Sleep");
        timer.Dispose();
        _timer = null;
        return false;
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _resolution.Dispose();
    }
}
