using ControllerMagic;
using Microsoft.Extensions.Time.Testing;

namespace ControllerMagic.Tests;

internal sealed class FakeHidHide : IHidHide
{
    public bool IsInstalled => true;
    public bool Cloaked { get; private set; }
    public int AllowListCalls { get; private set; }
    public List<string> BlockedPaths { get; } = [];
    public BlockResult NextBlockResult { get; set; } = BlockResult.AlreadyBlocked;

    public void EnsureAppAllowListed() => AllowListCalls++;

    public BlockResult BlockDevice(string deviceInterfacePath)
    {
        BlockedPaths.Add(deviceInterfacePath);
        return NextBlockResult;
    }

    public void SetCloakingEnabled(bool enabled) => Cloaked = enabled;
}

internal sealed class FakeVirtualPad : IVirtualPad
{
    public bool ConnectSucceeds { get; set; } = true;
    public bool IsConnected { get; private set; }
    public int ExcludedXInputSlots => 0;
    public int ConnectAttempts { get; private set; }
    public int ReportsSubmitted { get; private set; }

    public bool TryConnect()
    {
        ConnectAttempts++;
        IsConnected = ConnectSucceeds;
        return IsConnected;
    }

    public void SubmitReport(PadState pad, bool includeStickAndDpad = true) => ReportsSubmitted++;

    public void Disconnect() => IsConnected = false;

    // A failed submit makes the real bridge drop its connection.
    public void DropConnection() => IsConnected = false;

    public void Dispose() => IsConnected = false;
}

// Runs background work inline, or holds it back so a test can decide when it lands.
internal sealed class FakeBackgroundRunner
{
    private readonly Queue<Action> _held = new();

    public bool Hold { get; set; }

    public Task Run(Action action)
    {
        if (Hold)
        {
            _held.Enqueue(action);
            return Task.CompletedTask;
        }

        action();
        return Task.CompletedTask;
    }

    public void RunHeld()
    {
        while (_held.TryDequeue(out var action))
            action();
    }
}

internal sealed class PassthroughHarness
{
    public const int Serial = 1;
    public static readonly PhysicalDeviceIdentity Device = new(@"\\?\hid#vid_054c&pid_0ce6#fake", 0x054C, 0x0CE6);

    public FakeHidHide HidHide { get; } = new();
    public FakeVirtualPad VirtualPad { get; } = new();
    public FakeTimeProvider Clock { get; } = new();
    public FakeBackgroundRunner Runner { get; } = new();
    public bool SettingOn { get; set; } = true;
    public GamepadPassthroughController Controller { get; }

    public PassthroughHarness()
    {
        Controller = new GamepadPassthroughController(
            HidHide, VirtualPad, Clock, () => SettingOn, Runner.Run,
            _ => new DriverStatus(HidHideInstalled: true, VigemInstalled: true, NetworkAvailable: true));
    }

    public void TickWithPad() => Controller.Tick(default, gotPad: true, Device, Serial);

    public void TickWithoutPad() => Controller.Tick(default, gotPad: false, null, 0);
}
