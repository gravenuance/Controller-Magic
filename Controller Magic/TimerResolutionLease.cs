using System.Runtime.InteropServices;

namespace ControllerMagic;

// A 1 ms system timer resolution, raised only while held; timeEndPeriod runs only for a
// timeBeginPeriod that succeeded, so the two stay strictly balanced.
internal sealed class TimerResolutionLease : IDisposable
{
    private const uint PeriodMs = 1;
    private const uint TimerNoError = 0;

    private readonly Func<uint, uint> _begin;
    private readonly Func<uint, uint> _end;
    private bool _held;

    public TimerResolutionLease()
        : this(timeBeginPeriod, timeEndPeriod)
    {
    }

    internal TimerResolutionLease(Func<uint, uint> begin, Func<uint, uint> end)
    {
        _begin = begin;
        _end = end;
    }

    [DllImport("winmm.dll")]
    private static extern uint timeBeginPeriod(uint uPeriod);

    [DllImport("winmm.dll")]
    private static extern uint timeEndPeriod(uint uPeriod);

    public void Hold()
    {
        if (_held)
            return;
        _held = _begin(PeriodMs) == TimerNoError;
    }

    public void Release()
    {
        if (!_held)
            return;
        _held = false;
        _ = _end(PeriodMs);
    }

    public void Dispose() => Release();
}
