using ControllerMagic;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace ControllerMagic.Tests;

public class TouchpadMouseTests
{
    private const float PixelsPerPadWidth = 1000f;
    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(8);

    private readonly FakeTimeProvider _clock = new();
    private readonly TouchpadMouse _touchpad;

    public TouchpadMouseTests() => _touchpad = new TouchpadMouse(_clock);

    private TouchpadMouseOutput Touch(float x, float y, bool pressed = false) =>
        _touchpad.Update(touchActive: true, x, y, pressed, PixelsPerPadWidth);

    private TouchpadMouseOutput Lift(bool pressed = false) =>
        _touchpad.Update(touchActive: false, 0, 0, pressed, PixelsPerPadWidth);

    private void HoldStill(float x, float y, TimeSpan duration)
    {
        for (var held = TimeSpan.Zero; held < duration; held += Tick)
        {
            _clock.Advance(Tick);
            Touch(x, y);
        }
    }

    [Fact]
    public void QuickTap_LeftClicksOnLift()
    {
        Touch(0.5f, 0.5f);
        _clock.Advance(TimeSpan.FromMilliseconds(100));

        Assert.Equal(TouchClick.Left, Lift().Click);
    }

    [Fact]
    public void TwoQuickTaps_LeftClickTwiceForADoubleClick()
    {
        Touch(0.5f, 0.5f);
        _clock.Advance(TimeSpan.FromMilliseconds(80));
        var first = Lift();
        _clock.Advance(TimeSpan.FromMilliseconds(80));
        Touch(0.5f, 0.5f);
        _clock.Advance(TimeSpan.FromMilliseconds(80));
        var second = Lift();

        Assert.Equal(TouchClick.Left, first.Click);
        Assert.Equal(TouchClick.Left, second.Click);
    }

    [Fact]
    public void TapHeldPastTapLimit_DoesNotClick()
    {
        Touch(0.5f, 0.5f);
        HoldStill(0.5f, 0.5f, TouchpadMouse.TapMaxDuration + Tick);

        Assert.Equal(TouchClick.None, Lift().Click);
    }

    [Fact]
    public void Swipe_MovesCursorWithoutClicking()
    {
        Touch(0.2f, 0.5f);
        _clock.Advance(Tick);
        var move = Touch(0.3f, 0.5f);
        _clock.Advance(Tick);

        Assert.Equal(100, move.Dx);
        Assert.Equal(0, move.Dy);
        Assert.Equal(TouchClick.None, Lift().Click);
    }

    [Fact]
    public void FirstContact_DoesNotJumpTheCursor()
    {
        var first = Touch(0.9f, 0.9f);

        Assert.Equal(0, first.Dx);
        Assert.Equal(0, first.Dy);
    }

    [Fact]
    public void VerticalSlide_IsScaledToThePadsShorterHeight()
    {
        Touch(0.5f, 0.2f);
        var move = Touch(0.5f, 0.4f);

        Assert.Equal(0, move.Dx);
        Assert.Equal((int)(0.2f * TouchpadMouse.PadAspect * PixelsPerPadWidth), move.Dy);
    }

    [Fact]
    public void SlowSlide_AccumulatesSubPixelSteps()
    {
        // Power-of-two steps are exact in float: 16 steps of 1000/2048 px add up to 7.8 px.
        const float step = 1f / 2048;
        Touch(0.5f, 0.5f);
        int total = 0;
        for (int i = 1; i <= 16; i++)
            total += Touch(0.5f + i * step, 0.5f).Dx;

        Assert.Equal(7, total);
    }

    [Fact]
    public void HoldStill_RightClicksOnceAtLongPress()
    {
        Touch(0.5f, 0.5f);
        int rightClicks = 0;
        for (var held = TimeSpan.Zero; held < TouchpadMouse.LongPressDuration * 2; held += Tick)
        {
            _clock.Advance(Tick);
            if (Touch(0.5f, 0.5f).Click == TouchClick.Right)
                rightClicks++;
        }

        Assert.Equal(1, rightClicks);
        Assert.Equal(TouchClick.None, Lift().Click);
    }

    [Fact]
    public void HoldAfterMoving_DoesNotRightClick()
    {
        Touch(0.2f, 0.5f);
        Touch(0.4f, 0.5f);

        var clicks = new List<TouchClick>();
        for (var held = TimeSpan.Zero; held < TouchpadMouse.LongPressDuration * 2; held += Tick)
        {
            _clock.Advance(Tick);
            clicks.Add(Touch(0.4f, 0.5f).Click);
        }

        Assert.DoesNotContain(TouchClick.Right, clicks);
    }

    [Fact]
    public void PressingThePad_HoldsLeftWithoutTapOrLongPress()
    {
        Touch(0.5f, 0.5f);
        var held = Touch(0.5f, 0.5f, pressed: true);
        HoldStillPressed(TouchpadMouse.LongPressDuration * 2, out bool rightClicked);
        var release = Touch(0.5f, 0.5f);
        var lift = Lift();

        Assert.True(held.HoldLeft);
        Assert.False(rightClicked);
        Assert.False(release.HoldLeft);
        Assert.Equal(TouchClick.None, lift.Click);
    }

    private void HoldStillPressed(TimeSpan duration, out bool rightClicked)
    {
        rightClicked = false;
        for (var held = TimeSpan.Zero; held < duration; held += Tick)
        {
            _clock.Advance(Tick);
            rightClicked |= Touch(0.5f, 0.5f, pressed: true).Click == TouchClick.Right;
        }
    }

    [Fact]
    public void PressAndSlide_DragsWhileHoldingLeft()
    {
        Touch(0.2f, 0.5f, pressed: true);
        var drag = Touch(0.3f, 0.5f, pressed: true);

        Assert.True(drag.HoldLeft);
        Assert.Equal(100, drag.Dx);
    }

    [Fact]
    public void Reset_MidTouch_DropsThePendingTap()
    {
        Touch(0.5f, 0.5f);
        _touchpad.Reset();

        Assert.Equal(TouchClick.None, Lift().Click);
    }

    [Fact]
    public void NoTouch_DoesNothing()
    {
        var idle = Lift();

        Assert.Equal(new TouchpadMouseOutput(0, 0, TouchClick.None, HoldLeft: false), idle);
    }
}
