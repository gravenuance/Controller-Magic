using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Exceptions;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;
using Nefarius.ViGEm.Client.Targets.Xbox360.Exceptions;

namespace ControllerMagic;

// The virtual pad operations passthrough needs, so its state machine can run against a fake in tests.
internal interface IVirtualPad : IDisposable
{
    bool IsConnected { get; }
    int ExcludedXInputSlots { get; }
    bool TryConnect();
    void SubmitReport(PadState pad, bool includeStickAndDpad = true);
    void Disconnect();
}

// Owns one virtual Xbox 360 controller for the lifetime of the "suppress Guide button" mode.
// AutoSubmitReport is turned off so a whole PadState lands as one USB report instead of one
// report per SetButtonState/SetAxisValue call.
internal sealed class VigemBridge : IVirtualPad
{
    private const int AllXInputSlots = 0b1111;
    private static readonly TimeSpan UserIndexQueryInterval = TimeSpan.FromMilliseconds(100);

    // Reports and the slot query come from the poll thread while connects and disconnects come
    // from a transition's pool thread; the gate keeps a handle from being freed while in use.
    private readonly Lock _gate = new();
    private readonly Func<IDisposable> _createClient;
    private readonly Func<IDisposable, IXbox360Controller> _createController;
    private readonly Func<int> _connectedXInputSlots;
    private readonly TimeProvider _clock;
    private IDisposable? _client;
    private volatile IXbox360Controller? _controller;
    private int _slotsInUseBeforeConnect;
    private int? _userIndex;
    private DateTimeOffset _nextUserIndexQueryUtc;

    public VigemBridge()
        : this(() => new ViGEmClient(), client => ((ViGEmClient)client).CreateXbox360Controller(),
            XInputPadReader.ConnectedSlots, TimeProvider.System)
    {
    }

    internal VigemBridge(
        Func<IDisposable> createClient, Func<IDisposable, IXbox360Controller> createController,
        Func<int> connectedXInputSlots, TimeProvider clock)
    {
        _createClient = createClient;
        _createController = createController;
        _connectedXInputSlots = connectedXInputSlots;
        _clock = clock;
    }

    public bool IsConnected => _controller != null;

    // XInput slots that may hold this virtual pad, as a bit per slot; read every poll tick.
    // ViGEmBus reports the slot some time after Connect() (IXbox360Controller.UserIndex throws
    // Xbox360UserIndexNotReportedException until then), so until it does, every slot that was free
    // before connecting counts - otherwise XInput reads this app's own pad back as the real one.
    // The slot is asked for at a low rate and kept once known, since the ask throws until then.
    public int ExcludedXInputSlots
    {
        get
        {
            lock (_gate)
            {
                if (_controller == null)
                    return 0;

                if (_userIndex is null && _clock.GetUtcNow() >= _nextUserIndexQueryUtc)
                {
                    _userIndex = QueryUserIndex(_controller);
                    _nextUserIndexQueryUtc = _clock.GetUtcNow() + UserIndexQueryInterval;
                }

                return _userIndex is int slot ? 1 << slot : ~_slotsInUseBeforeConnect & AllXInputSlots;
            }
        }
    }

    private static int? QueryUserIndex(IXbox360Controller controller)
    {
        try
        {
            int slot = controller.UserIndex;
            return slot is >= 0 and <= 3 ? slot : null;
        }
        catch (Xbox360UserIndexNotReportedException)
        {
            return null;
        }
    }

    public bool TryConnect()
    {
        if (_controller != null)
            return true;

        IDisposable? client = null;
        IXbox360Controller? controller = null;
        int slotsInUseBeforeConnect;
        try
        {
            slotsInUseBeforeConnect = _connectedXInputSlots();
            client = _createClient();
            controller = _createController(client);
            controller.AutoSubmitReport = false;
            controller.Connect();
        }
        catch (VigemBusNotFoundException ex)
        {
            AppLog.Default.Warning("VigemBridge: ViGEmBus driver not found", ex);
            Release(controller, client, wasConnected: false);
            return false;
        }
        catch (Exception ex)
        {
            AppLog.Default.Warning("VigemBridge: failed to connect a virtual controller", ex);
            Release(controller, client, wasConnected: false);
            return false;
        }

        // Built outside the gate so a slow driver call never holds up the poll thread.
        lock (_gate)
        {
            _client = client;
            _controller = controller;
            _slotsInUseBeforeConnect = slotsInUseBeforeConnect;
            _userIndex = null;
            _nextUserIndexQueryUtc = DateTimeOffset.MinValue;
        }

        return true;
    }

    // includeStickAndDpad=false keeps the left stick and D-pad neutral on the virtual pad while
    // this app's own stick-as-mouse control is active, so Windows' gamepad-driven UI focus
    // navigation (see VirtualPadReportMapper) doesn't fight with it. The right stick and triggers
    // are unaffected - only the D-pad and left stick drive that Windows feature.
    public void SubmitReport(PadState pad, bool includeStickAndDpad = true)
    {
        lock (_gate)
        {
            var controller = _controller;
            if (controller == null)
                return;

            try
            {
                foreach (var (button, pressed) in VirtualPadReportMapper.MapButtons(pad.Buttons, includeStickAndDpad))
                    controller.SetButtonState(button, pressed);

                controller.SetAxisValue(Xbox360Axis.LeftThumbX, includeStickAndDpad ? pad.LeftThumbX : (short)0);
                controller.SetAxisValue(Xbox360Axis.LeftThumbY, includeStickAndDpad ? pad.LeftThumbY : (short)0);
                controller.SetAxisValue(Xbox360Axis.RightThumbX, pad.RightThumbX);
                controller.SetAxisValue(Xbox360Axis.RightThumbY, pad.RightThumbY);
                controller.SetSliderValue(Xbox360Slider.LeftTrigger, pad.LeftTrigger);
                controller.SetSliderValue(Xbox360Slider.RightTrigger, pad.RightTrigger);
                controller.SubmitReport();
                return;
            }
            catch (Exception ex)
            {
                AppLog.Default.Warning("VigemBridge: failed to submit a report; disconnecting", ex);
            }
        }

        Cleanup();
    }

    public void Disconnect() => Cleanup();

    // Detaches under the gate, then releases outside it: nothing else can reach the handles by then.
    private void Cleanup()
    {
        IDisposable? client;
        IXbox360Controller? controller;
        lock (_gate)
        {
            client = _client;
            controller = _controller;
            _client = null;
            _controller = null;
        }

        Release(controller, client, wasConnected: controller != null);
    }

    // The target's finalizer frees it through its client, so it's disposed first, while the
    // client is still alive; left to the finalizer it would run against a disposed client.
    private static void Release(IXbox360Controller? controller, IDisposable? client, bool wasConnected)
    {
        if (wasConnected)
        {
            try
            {
                controller?.Disconnect();
            }
            catch (Exception ex)
            {
                AppLog.Default.Warning("VigemBridge: error disconnecting the virtual controller", ex);
            }
        }

        try
        {
            (controller as IDisposable)?.Dispose();
        }
        catch (Exception ex)
        {
            AppLog.Default.Warning("VigemBridge: error disposing the virtual controller", ex);
        }

        try
        {
            client?.Dispose();
        }
        catch (Exception ex)
        {
            AppLog.Default.Warning("VigemBridge: error disposing the ViGEm client", ex);
        }
    }

    public void Dispose() => Cleanup();
}
