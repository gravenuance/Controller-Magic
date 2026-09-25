using ControllerMagic;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace ControllerMagic.Tests;

public class PlayerLightsScheduleTests
{
    private const byte Full = 0x1F;
    private const byte Medium = 0x15;
    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(8);
    private readonly FakeTimeProvider _clock = new();

    private PlayerLightsSchedule NewOpenedSchedule()
    {
        var schedule = new PlayerLightsSchedule(_clock);
        schedule.Restart();
        return schedule;
    }

    // Polls the same pattern every tick, as the poll loop does, and returns when each send happened.
    private List<TimeSpan> SendTimesOver(PlayerLightsSchedule schedule, TimeSpan duration, byte mask = Full)
    {
        var sends = new List<TimeSpan>();
        for (var elapsed = TimeSpan.Zero; elapsed <= duration; elapsed += Tick)
        {
            if (schedule.Decide(mask) != PlayerLightsSend.None)
                sends.Add(elapsed);
            _clock.Advance(Tick);
        }

        return sends;
    }

    [Fact]
    public void Decide_FirstPatternAfterOpen_IsSent()
    {
        Assert.Equal(PlayerLightsSend.Changed, NewOpenedSchedule().Decide(Full));
    }

    [Fact]
    public void Decide_SamePatternShortlyAfter_IsNotResent()
    {
        var schedule = NewOpenedSchedule();
        schedule.Decide(Full);

        _clock.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal(PlayerLightsSend.None, schedule.Decide(Full));
    }

    [Fact]
    public void Decide_SamePattern_RefreshedEveryFewSecondsUntilPastSdlsLedReset()
    {
        var sends = SendTimesOver(NewOpenedSchedule(), TimeSpan.FromSeconds(15));

        Assert.True(sends.Count >= 5, $"only {sends.Count} sends in the first 15 s");
        Assert.All(sends.Zip(sends.Skip(1)), pair => Assert.InRange(pair.Second - pair.First, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4)));
        Assert.True(sends[^1] >= TimeSpan.FromSeconds(12), $"last refresh at {sends[^1]}, before SDL's reset at ~10.2 s could be covered");
    }

    [Fact]
    public void Decide_SamePatternAfterTheInterval_IsARefresh()
    {
        var schedule = NewOpenedSchedule();
        schedule.Decide(Full);

        _clock.Advance(TimeSpan.FromSeconds(3));

        Assert.Equal(PlayerLightsSend.Refresh, schedule.Decide(Full));
    }

    [Fact]
    public void Decide_SamePattern_OneRefreshLandsAtOrAfterTheWindowEnd()
    {
        var schedule = NewOpenedSchedule();

        var sends = SendTimesOver(schedule, TimeSpan.FromSeconds(20));

        Assert.Contains(sends, t => t >= TimeSpan.FromSeconds(15));
    }

    [Fact]
    public void Decide_SamePattern_NeverRefreshedOnceTheWindowIsOver()
    {
        var schedule = NewOpenedSchedule();
        SendTimesOver(schedule, TimeSpan.FromSeconds(20));

        Assert.Empty(SendTimesOver(schedule, TimeSpan.FromMinutes(2)));
    }

    [Fact]
    public void Decide_ChangedPatternAfterTheWindow_IsSentOnce()
    {
        var schedule = NewOpenedSchedule();
        SendTimesOver(schedule, TimeSpan.FromSeconds(20));

        Assert.Equal(PlayerLightsSend.Changed, schedule.Decide(Medium));
        Assert.Empty(SendTimesOver(schedule, TimeSpan.FromSeconds(30), Medium));
    }

    [Fact]
    public void Decide_ChangedPatternInsideTheRefreshInterval_IsSentImmediately()
    {
        var schedule = NewOpenedSchedule();
        schedule.Decide(Full);

        _clock.Advance(Tick);

        Assert.Equal(PlayerLightsSend.Changed, schedule.Decide(Medium));
    }

    [Fact]
    public void Restart_AfterReopen_SendsAgainAndRefreshesForANewWindow()
    {
        var schedule = NewOpenedSchedule();
        SendTimesOver(schedule, TimeSpan.FromSeconds(20));

        schedule.Restart();
        var sends = SendTimesOver(schedule, TimeSpan.FromSeconds(15));

        Assert.Equal(TimeSpan.Zero, sends[0]);
        Assert.True(sends.Count >= 5, $"only {sends.Count} sends after the reopen");
    }
}
