using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace ControllerMagic.Tests;

public sealed class KeyEchoAnimationTests
{
    private readonly FakeTimeProvider _clock = new();
    private readonly KeyEchoAnimation _animation;

    public KeyEchoAnimationTests() => _animation = new KeyEchoAnimation(_clock);

    [Fact]
    public void Current_BeforeAnyLetter_IsNull()
    {
        Assert.Null(_animation.Current());
    }

    [Fact]
    public void Current_RightAfterTyping_IsFullyOpaqueAtFullSize()
    {
        _animation.Start();

        Assert.Equal(new EchoFrame(Opacity: 1, Scale: 1), _animation.Current());
    }

    [Fact]
    public void Current_HalfwayThrough_HasShrunkFasterThanItHasFaded()
    {
        _animation.Start();
        _clock.Advance(TimeSpan.FromMilliseconds(350));

        Assert.Equal(new EchoFrame(Opacity: 0.75, Scale: 0.625), _animation.Current());
    }

    [Fact]
    public void Current_AfterTheFullDuration_IsNullFromThenOn()
    {
        _animation.Start();
        _clock.Advance(TimeSpan.FromMilliseconds(700));

        Assert.Null(_animation.Current());
        _clock.Advance(TimeSpan.FromMilliseconds(16));
        Assert.Null(_animation.Current());
    }

    [Fact]
    public void Start_WhileFading_RestartsAtFullSize()
    {
        _animation.Start();
        _clock.Advance(TimeSpan.FromMilliseconds(500));

        _animation.Start();

        Assert.Equal(new EchoFrame(Opacity: 1, Scale: 1), _animation.Current());
    }
}
