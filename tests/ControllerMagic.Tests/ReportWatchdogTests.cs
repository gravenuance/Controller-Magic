using ControllerMagic;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace ControllerMagic.Tests;

public class ReportWatchdogTests
{
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMilliseconds(200);
    private readonly FakeTimeProvider _clock = new();

    private ReportWatchdog NewWatchdog() => new(_clock, StaleAfter);

    [Fact]
    public void IsStale_StampKeepsAdvancing_StaysLive()
    {
        var watchdog = NewWatchdog();

        for (ulong stamp = 1; stamp <= 100; stamp++)
        {
            _clock.Advance(TimeSpan.FromMilliseconds(8));
            Assert.False(watchdog.IsStale(stamp));
        }
    }

    [Fact]
    public void IsStale_StampFrozenJustUnderLimit_StaysLive()
    {
        var watchdog = NewWatchdog();
        watchdog.IsStale(5);

        _clock.Advance(StaleAfter - TimeSpan.FromMilliseconds(1));

        Assert.False(watchdog.IsStale(5));
    }

    [Fact]
    public void IsStale_StampFrozenForLimit_IsStale()
    {
        var watchdog = NewWatchdog();
        watchdog.IsStale(5);

        _clock.Advance(StaleAfter);

        Assert.True(watchdog.IsStale(5));
    }

    [Fact]
    public void IsStale_ReportsResumeAfterGoingStale_IsLiveAgain()
    {
        var watchdog = NewWatchdog();
        watchdog.IsStale(5);
        _clock.Advance(TimeSpan.FromSeconds(2));
        Assert.True(watchdog.IsStale(5));

        Assert.False(watchdog.IsStale(6));
    }

    [Fact]
    public void IsStale_NoLivenessSignal_NeverStale()
    {
        var watchdog = NewWatchdog();
        watchdog.IsStale(null);

        _clock.Advance(TimeSpan.FromMinutes(5));

        Assert.False(watchdog.IsStale(null));
    }

    [Fact]
    public void IsStale_AfterReset_FirstStampStartsFresh()
    {
        var watchdog = NewWatchdog();
        watchdog.IsStale(5);
        _clock.Advance(TimeSpan.FromSeconds(2));

        watchdog.Reset();

        Assert.False(watchdog.IsStale(5));
    }

    // Regression: a pad sending reports without the stamp was held neutral for the whole connection.
    [Fact]
    public void IsStale_InputChangesUnderAFrozenStamp_IsLiveAndStopsJudgingByTheStamp()
    {
        var watchdog = NewWatchdog();
        watchdog.IsStale(5);
        _clock.Advance(TimeSpan.FromSeconds(2));

        bool whenInputChanged = watchdog.IsStale(5, inputChanged: true);
        _clock.Advance(TimeSpan.FromSeconds(2));
        bool afterwards = watchdog.IsStale(5, inputChanged: false);

        Assert.False(whenInputChanged);
        Assert.False(afterwards);
        Assert.True(watchdog.IgnoresStamps);
    }

    [Fact]
    public void IsStale_InputChangesWithinTheLimit_KeepsJudgingByTheStamp()
    {
        var watchdog = NewWatchdog();
        watchdog.IsStale(5);
        watchdog.IsStale(5, inputChanged: true);

        _clock.Advance(StaleAfter);

        Assert.True(watchdog.IsStale(5));
        Assert.False(watchdog.IgnoresStamps);
    }

    [Fact]
    public void Reset_AfterStampsWereIgnored_JudgesByTheStampAgain()
    {
        var watchdog = NewWatchdog();
        watchdog.IsStale(5);
        _clock.Advance(TimeSpan.FromSeconds(2));
        watchdog.IsStale(5, inputChanged: true);

        watchdog.Reset();
        watchdog.IsStale(7);
        _clock.Advance(StaleAfter);

        Assert.True(watchdog.IsStale(7));
    }

    [Fact]
    public void Constructor_NonPositiveLimit_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ReportWatchdog(_clock, TimeSpan.Zero));
    }
}
