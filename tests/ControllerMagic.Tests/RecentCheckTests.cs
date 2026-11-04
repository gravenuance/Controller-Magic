using ControllerMagic;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace ControllerMagic.Tests;

public class RecentCheckTests
{
    private static readonly TimeSpan FreshFor = TimeSpan.FromMinutes(1);
    private readonly FakeTimeProvider _clock = new();

    private RecentCheck NewCheck() => new(_clock, FreshFor);

    [Fact]
    public void IsFresh_NeverCompleted_IsFalse() => Assert.False(NewCheck().IsFresh);

    [Fact]
    public void IsFresh_JustUnderTheLimitAfterCompleting_IsTrue()
    {
        var check = NewCheck();
        check.MarkCompleted();

        _clock.Advance(FreshFor - TimeSpan.FromSeconds(1));

        Assert.True(check.IsFresh);
    }

    [Fact]
    public void IsFresh_AtTheLimit_IsFalse()
    {
        var check = NewCheck();
        check.MarkCompleted();

        _clock.Advance(FreshFor);

        Assert.False(check.IsFresh);
    }

    [Fact]
    public void Invalidate_AfterCompleting_IsNotFresh()
    {
        var check = NewCheck();
        check.MarkCompleted();

        check.Invalidate();

        Assert.False(check.IsFresh);
    }
}
