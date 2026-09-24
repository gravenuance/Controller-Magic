using ControllerMagic;
using Xunit;

namespace ControllerMagic.Tests;

public class PadConnectionTrackerTests
{
    private static readonly PhysicalDeviceIdentity DualSense = new(@"\\?\hid#vid_054c&pid_0ce6#1", 0x054C, 0x0CE6);

    [Fact]
    public void Observe_XInputPad_IdentifiesItBySlotPlaceholder()
    {
        var tracker = new PadConnectionTracker();

        tracker.Observe(PadSource.XInput, xinputSlot: 2, sdlIdentity: null, sdlSerial: 0);

        Assert.Equal("XInput#2", tracker.Identity?.InterfacePath);
        Assert.True(PhysicalDeviceIdentity.IsXInputPlaceholderPath(tracker.Identity!.Value.InterfacePath));
    }

    [Fact]
    public void Observe_SamePadEveryTick_KeepsOneSerial()
    {
        var tracker = new PadConnectionTracker();
        tracker.Observe(PadSource.XInput, 0, null, 0);
        int serial = tracker.Serial;

        for (int i = 0; i < 10; i++)
            tracker.Observe(PadSource.XInput, 0, null, 0);

        Assert.Equal(serial, tracker.Serial);
    }

    [Fact]
    public void Observe_XInputPadReconnects_IsANewConnection()
    {
        var tracker = new PadConnectionTracker();
        tracker.Observe(PadSource.XInput, 0, null, 0);
        int first = tracker.Serial;

        tracker.Observe(PadSource.None, 0, null, 0);
        tracker.Observe(PadSource.XInput, 0, null, 0);

        Assert.NotEqual(first, tracker.Serial);
    }

    [Fact]
    public void Observe_XInputSlotChanges_IsANewConnection()
    {
        var tracker = new PadConnectionTracker();
        tracker.Observe(PadSource.XInput, 0, null, 0);
        int first = tracker.Serial;

        tracker.Observe(PadSource.XInput, 1, null, 0);

        Assert.NotEqual(first, tracker.Serial);
        Assert.Equal("XInput#1", tracker.Identity?.InterfacePath);
    }

    [Fact]
    public void Observe_SdlReopens_IsANewConnection()
    {
        var tracker = new PadConnectionTracker();
        tracker.Observe(PadSource.Sdl, 0, DualSense, sdlSerial: 1);
        int first = tracker.Serial;

        tracker.Observe(PadSource.Sdl, 0, DualSense, sdlSerial: 2);

        Assert.NotEqual(first, tracker.Serial);
        Assert.Equal(DualSense, tracker.Identity);
    }

    [Fact]
    public void Observe_SwitchingReaders_IsANewConnection()
    {
        var tracker = new PadConnectionTracker();
        tracker.Observe(PadSource.Sdl, 0, DualSense, 1);
        int first = tracker.Serial;

        tracker.Observe(PadSource.XInput, 0, DualSense, 1);

        Assert.NotEqual(first, tracker.Serial);
    }

    [Fact]
    public void Observe_NoPad_ClearsIdentity()
    {
        var tracker = new PadConnectionTracker();
        tracker.Observe(PadSource.Sdl, 0, DualSense, 1);

        tracker.Observe(PadSource.None, 0, DualSense, 1);

        Assert.Null(tracker.Identity);
    }
}
