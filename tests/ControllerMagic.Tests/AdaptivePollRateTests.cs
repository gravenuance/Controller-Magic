using ControllerMagic;
using Xunit;

namespace ControllerMagic.Tests;

public sealed class AdaptivePollRateTests
{
    private const int Fast = 8;
    private const int Slow = 33;
    private const long RestAfter = 2000;

    private readonly AdaptivePollRate _rate = new(Fast, Slow, RestAfter);

    [Fact]
    public void Next_FirstObservation_IsFastEvenWhenInactive() =>
        Assert.Equal(Fast, _rate.Next(active: false, nowMs: 1_000_000));

    [Fact]
    public void Next_InactiveLessThanTheThreshold_StaysFast()
    {
        _rate.Next(active: true, nowMs: 0);

        Assert.Equal(Fast, _rate.Next(active: false, nowMs: RestAfter - 1));
    }

    [Fact]
    public void Next_InactiveForTheThreshold_SlowsDown()
    {
        _rate.Next(active: true, nowMs: 0);

        Assert.Equal(Slow, _rate.Next(active: false, nowMs: RestAfter));
        Assert.Equal(Slow, _rate.Next(active: false, nowMs: RestAfter + 5_000));
    }

    [Fact]
    public void Next_AnyActivityWhileResting_ReturnsToFastAtOnce()
    {
        _rate.Next(active: true, nowMs: 0);
        _rate.Next(active: false, nowMs: 10_000);

        Assert.Equal(Fast, _rate.Next(active: true, nowMs: 10_033));
    }

    [Fact]
    public void Next_ActivityRestartsTheWait()
    {
        _rate.Next(active: true, nowMs: 0);
        _rate.Next(active: true, nowMs: 10_000);

        Assert.Equal(Fast, _rate.Next(active: false, nowMs: 10_000 + RestAfter - 1));
        Assert.Equal(Slow, _rate.Next(active: false, nowMs: 10_000 + RestAfter));
    }

    [Fact]
    public void Next_AfterReset_StartsFastAndWaitsAgain()
    {
        _rate.Next(active: true, nowMs: 0);
        _rate.Next(active: false, nowMs: 10_000);
        _rate.Reset();

        Assert.Equal(Fast, _rate.Next(active: false, nowMs: 20_000));
        Assert.Equal(Fast, _rate.Next(active: false, nowMs: 20_000 + RestAfter - 1));
        Assert.Equal(Slow, _rate.Next(active: false, nowMs: 20_000 + RestAfter));
    }
}
