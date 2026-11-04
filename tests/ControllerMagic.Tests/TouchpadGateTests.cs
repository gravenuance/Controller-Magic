using ControllerMagic;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace ControllerMagic.Tests;

public class TouchpadGateTests
{
    private static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(300);
    private readonly FakeTimeProvider _clock = new();

    private TouchpadGate NewGate() => new(_clock, Quiet);

    [Fact]
    public void Allows_TouchWithNoOtherInput_IsAllowed()
    {
        var gate = NewGate();

        Assert.True(gate.Allows(touchActive: true, otherInputActive: false));
    }

    [Fact]
    public void Allows_TouchDuringOtherInput_IsBlocked()
    {
        var gate = NewGate();

        Assert.False(gate.Allows(touchActive: true, otherInputActive: true));
    }

    [Fact]
    public void Allows_TouchStartedWithinQuietTime_StaysBlockedUntilLifted()
    {
        var gate = NewGate();
        gate.Allows(touchActive: false, otherInputActive: true);

        _clock.Advance(TimeSpan.FromMilliseconds(299));
        bool atStart = gate.Allows(touchActive: true, otherInputActive: false);
        _clock.Advance(TimeSpan.FromSeconds(1));
        bool later = gate.Allows(touchActive: true, otherInputActive: false);

        Assert.False(atStart);
        Assert.False(later);
    }

    [Fact]
    public void Allows_TouchStartedAfterQuietTime_IsAllowed()
    {
        var gate = NewGate();
        gate.Allows(touchActive: false, otherInputActive: true);

        _clock.Advance(Quiet);

        Assert.True(gate.Allows(touchActive: true, otherInputActive: false));
    }

    [Fact]
    public void Allows_TouchInProgressWhenOtherInputStarts_StaysBlockedUntilLiftedThenAllowsANewTouch()
    {
        var gate = NewGate();
        gate.Allows(touchActive: true, otherInputActive: false);
        gate.Allows(touchActive: true, otherInputActive: true);

        _clock.Advance(TimeSpan.FromSeconds(1));
        bool sameTouch = gate.Allows(touchActive: true, otherInputActive: false);
        gate.Allows(touchActive: false, otherInputActive: false);
        bool newTouch = gate.Allows(touchActive: true, otherInputActive: false);

        Assert.False(sameTouch);
        Assert.True(newTouch);
    }
}
