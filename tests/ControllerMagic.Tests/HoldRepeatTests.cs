using ControllerMagic;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace ControllerMagic.Tests;

public class HoldRepeatTests
{
    private static readonly TimeSpan Delay = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(100);
    private readonly FakeTimeProvider _clock = new();

    private HoldRepeat NewRepeat() => new(_clock, Delay, Interval);

    [Fact]
    public void Fires_OnPress_FiresAtOnce()
    {
        Assert.True(NewRepeat().Fires(held: true));
    }

    [Fact]
    public void Fires_HeldJustUnderTheDelay_DoesNotRepeat()
    {
        var repeat = NewRepeat();
        repeat.Fires(held: true);

        _clock.Advance(TimeSpan.FromMilliseconds(399));

        Assert.False(repeat.Fires(held: true));
    }

    [Fact]
    public void Fires_HeldPastTheDelay_RepeatsAtTheInterval()
    {
        var repeat = NewRepeat();
        repeat.Fires(held: true);

        _clock.Advance(Delay);
        bool first = repeat.Fires(held: true);
        _clock.Advance(TimeSpan.FromMilliseconds(99));
        bool tooSoon = repeat.Fires(held: true);
        _clock.Advance(TimeSpan.FromMilliseconds(1));
        bool second = repeat.Fires(held: true);

        Assert.True(first);
        Assert.False(tooSoon);
        Assert.True(second);
    }

    [Fact]
    public void Fires_Released_DoesNotFireAndTheNextPressFiresAtOnce()
    {
        var repeat = NewRepeat();
        repeat.Fires(held: true);

        bool released = repeat.Fires(held: false);
        bool pressedAgain = repeat.Fires(held: true);

        Assert.False(released);
        Assert.True(pressedAgain);
    }
}
