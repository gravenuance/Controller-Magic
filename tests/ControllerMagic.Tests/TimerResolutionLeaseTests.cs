using ControllerMagic;
using Xunit;

namespace ControllerMagic.Tests;

public class TimerResolutionLeaseTests
{
    private int _begins;
    private int _ends;
    private uint _beginResult;

    private TimerResolutionLease NewLease() =>
        new(_ => { _begins++; return _beginResult; }, _ => { _ends++; return 0; });

    [Fact]
    public void HoldTwice_RaisesTheResolutionOnce()
    {
        using var lease = NewLease();

        lease.Hold();
        lease.Hold();

        Assert.Equal(1, _begins);
    }

    [Fact]
    public void Release_EndsExactlyWhatWasBegun()
    {
        var lease = NewLease();

        lease.Release();
        lease.Hold();
        lease.Release();
        lease.Release();
        lease.Dispose();

        Assert.Equal(1, _begins);
        Assert.Equal(1, _ends);
    }

    [Fact]
    public void HoldAgainAfterRelease_RaisesItAgain()
    {
        using var lease = NewLease();

        lease.Hold();
        lease.Release();
        lease.Hold();

        Assert.Equal(2, _begins);
        Assert.Equal(1, _ends);
    }

    [Fact]
    public void FailedBegin_IsNeverEnded()
    {
        _beginResult = 97; // TIMERR_NOCANDO
        var lease = NewLease();

        lease.Hold();
        lease.Dispose();

        Assert.Equal(0, _ends);
    }

    [Fact]
    public void Dispose_ReleasesAHeldResolution()
    {
        var lease = NewLease();
        lease.Hold();

        lease.Dispose();

        Assert.Equal(1, _ends);
    }
}
