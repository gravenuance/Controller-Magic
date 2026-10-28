using ControllerMagic;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace ControllerMagic.Tests;

public sealed class IntervalGateTests
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);

    private readonly FakeTimeProvider _clock = new();
    private readonly IntervalGate _gate;

    public IntervalGateTests() => _gate = new IntervalGate(_clock, Interval);

    [Fact]
    public void TryEnter_FirstCall_IsTrue() => Assert.True(_gate.TryEnter());

    [Fact]
    public void TryEnter_WithinTheInterval_IsFalse()
    {
        _gate.TryEnter();
        _clock.Advance(Interval - TimeSpan.FromMilliseconds(1));

        Assert.False(_gate.TryEnter());
    }

    [Fact]
    public void TryEnter_AfterTheInterval_IsTrueOncePerInterval()
    {
        _gate.TryEnter();
        _clock.Advance(Interval);

        Assert.True(_gate.TryEnter());
        Assert.False(_gate.TryEnter());
    }
}
