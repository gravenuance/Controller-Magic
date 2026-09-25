using ControllerMagic;
using Xunit;

namespace ControllerMagic.Tests;

public sealed class PassthroughResourceCheckTests : IDisposable
{
    // Wall clock and monotonic timestamp set independently, as when the user changes the system time.
    private sealed class SettableClock : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

        public long Timestamp { get; set; } = 1_000_000;

        public override DateTimeOffset GetUtcNow() => UtcNow;

        public override long GetTimestamp() => Timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public void Advance(TimeSpan by)
        {
            UtcNow += by;
            Timestamp += by.Ticks;
        }
    }

    private readonly SettableClock _clock = new();
    private int _checks;
    private readonly FakeVirtualPad _virtualPad = new();
    private readonly GamepadPassthroughController _controller;

    public PassthroughResourceCheckTests()
    {
        var runner = new FakeBackgroundRunner();
        _controller = new GamepadPassthroughController(
            new FakeHidHide(), _virtualPad, _clock, () => false, runner.Run,
            _ => new DriverStatus(HidHideInstalled: true, VigemInstalled: true, NetworkAvailable: true),
            () =>
            {
                _checks++;
                return 0;
            });
    }

    public void Dispose()
    {
        _controller.Dispose();
        _virtualPad.Dispose();
    }

    private void Tick() => _controller.Tick(default, gotPad: false, null, 0);

    [Fact]
    public void ResourceCheck_RunsAtMostEveryHalfSecond()
    {
        Tick();
        _clock.Advance(TimeSpan.FromMilliseconds(400));
        Tick();
        _clock.Advance(TimeSpan.FromMilliseconds(100));
        Tick();

        Assert.Equal(2, _checks);
    }

    [Fact]
    public void ResourceCheck_SystemClockSetBack_KeepsRunning()
    {
        Tick();
        _clock.UtcNow -= TimeSpan.FromHours(1);
        _clock.Timestamp += TimeSpan.FromMilliseconds(600).Ticks;
        Tick();

        Assert.Equal(2, _checks);
    }
}
