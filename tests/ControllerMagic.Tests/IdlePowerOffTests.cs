using ControllerMagic;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace ControllerMagic.Tests;

public sealed class IdlePowerOffTests
{
    private const ulong Address = 0x0C27568B2203;
    private static readonly IdleThresholds Thresholds = new(StickDeadZone: 4000, ScrollDeadZone: 4000);
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(15);
    private static readonly PadState Resting = new() { IsConnected = true };

    private readonly FakeTimeProvider _clock = new();
    private readonly FakePowerOff _power = new();
    private readonly IdlePowerOff _idle;

    public IdlePowerOffTests() => _idle = new IdlePowerOff(_clock, _power);

    private void ObserveAfter(TimeSpan elapsed, PadState pad, ulong? address = Address, TimeSpan? timeout = null)
    {
        _clock.Advance(elapsed);
        _idle.Observe(pad, address, Thresholds, timeout ?? Timeout);
    }

    [Fact]
    public void Observe_UnusedForTheTimeout_PowersOffOnce()
    {
        ObserveAfter(TimeSpan.Zero, Resting);
        ObserveAfter(Timeout, Resting);
        ObserveAfter(TimeSpan.FromMinutes(5), Resting);

        Assert.Equal([Address], _power.Calls);
    }

    [Fact]
    public void Observe_UnusedForLessThanTheTimeout_LeavesItOn()
    {
        ObserveAfter(TimeSpan.Zero, Resting);
        ObserveAfter(Timeout - TimeSpan.FromSeconds(1), Resting);

        Assert.Empty(_power.Calls);
    }

    [Fact]
    public void Observe_UseDuringTheWait_RestartsIt()
    {
        ObserveAfter(TimeSpan.Zero, Resting);
        ObserveAfter(TimeSpan.FromMinutes(10), Resting with { Buttons = PadButtons.A });
        ObserveAfter(TimeSpan.FromMinutes(10), Resting);

        Assert.Empty(_power.Calls);
    }

    [Fact]
    public void Observe_ZeroTimeout_NeverPowersOff()
    {
        ObserveAfter(TimeSpan.Zero, Resting, timeout: TimeSpan.Zero);
        ObserveAfter(TimeSpan.FromHours(5), Resting, timeout: TimeSpan.Zero);

        Assert.Empty(_power.Calls);
    }

    [Fact]
    public void Observe_NoBluetoothAddress_NeverPowersOff()
    {
        ObserveAfter(TimeSpan.Zero, Resting, address: null);
        ObserveAfter(Timeout, Resting, address: null);

        Assert.Empty(_power.Calls);
    }

    // A USB pad has no link to drop; trying again every tick would only repeat the failure.
    [Fact]
    public void Observe_PowerOffFails_WaitsForUseBeforeTryingAgain()
    {
        _power.Succeeds = false;
        ObserveAfter(TimeSpan.Zero, Resting);
        ObserveAfter(Timeout, Resting);
        ObserveAfter(Timeout, Resting);
        ObserveAfter(TimeSpan.FromSeconds(1), Resting with { Buttons = PadButtons.A });
        ObserveAfter(Timeout, Resting);

        Assert.Equal([Address, Address], _power.Calls);
    }

    // A fullscreen game was using the pad, so the wait starts over once the app has it back.
    [Fact]
    public void Reset_StartsTheWaitAgain()
    {
        ObserveAfter(TimeSpan.Zero, Resting);
        ObserveAfter(TimeSpan.FromMinutes(14), Resting);
        _idle.Reset();
        ObserveAfter(TimeSpan.FromMinutes(2), Resting);
        ObserveAfter(TimeSpan.FromMinutes(14), Resting);

        Assert.Empty(_power.Calls);
    }

    [Theory]
    [InlineData("button")]
    [InlineData("touch")]
    [InlineData("left trigger")]
    [InlineData("right trigger")]
    [InlineData("left stick")]
    [InlineData("right stick")]
    public void IsActive_AnythingTheAppWouldActOn_Counts(string what)
    {
        var pad = what switch
        {
            "button" => Resting with { Buttons = PadButtons.Start },
            "touch" => Resting with { TouchActive = true },
            "left trigger" => Resting with { LeftTrigger = 31 },
            "right trigger" => Resting with { RightTrigger = 200 },
            "left stick" => Resting with { LeftThumbX = 4000 },
            "right stick" => Resting with { RightThumbY = -4000 },
            _ => throw new ArgumentOutOfRangeException(nameof(what)),
        };

        Assert.True(IdlePowerOff.IsActive(pad, Thresholds));
    }

    // Drift inside the deadzones, or a resting trigger, must not keep the pad on forever.
    [Fact]
    public void IsActive_DriftInsideTheDeadZones_DoesNotCount() =>
        Assert.False(IdlePowerOff.IsActive(
            Resting with { LeftThumbX = 2500, LeftThumbY = -2500, RightThumbX = 3000, LeftTrigger = 30 }, Thresholds));

    [Theory]
    [InlineData("0c:27:56:8b:22:03")]
    [InlineData("0C-27-56-8B-22-03")]
    [InlineData("0c27568b2203")]
    public void ParseBluetoothAddress_CommonForms_AreRead(string serial) =>
        Assert.Equal(Address, IdlePowerOff.ParseBluetoothAddress(serial));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("00:00:00:00:00:00")]
    [InlineData("0c:27:56:8b:22")]
    [InlineData("not-a-mac-addr")]
    [InlineData("0c:27:56:8b:22:03:44")]
    public void ParseBluetoothAddress_AnythingElse_IsNull(string? serial) =>
        Assert.Null(IdlePowerOff.ParseBluetoothAddress(serial));

    private sealed class FakePowerOff : IControllerPowerOff
    {
        public List<ulong> Calls { get; } = [];

        public bool Succeeds { get; set; } = true;

        public bool TryPowerOff(ulong bluetoothAddress)
        {
            Calls.Add(bluetoothAddress);
            return Succeeds;
        }
    }
}
