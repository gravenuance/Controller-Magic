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
    int? UserIndex { get; }
    bool TryConnect();
    void SubmitReport(PadState pad, bool includeStickAndDpad = true);
    void Disconnect();
}

// Owns one virtual Xbox 360 controller for the lifetime of the "suppress Guide button" mode.
// AutoSubmitReport is turned off so a whole PadState lands as one USB report instead of one
// report per SetButtonState/SetAxisValue call.
internal sealed class VigemBridge : IVirtualPad
{
    // Reports and UserIndex come from the poll thread while connects and disconnects come from a
    // transition's pool thread; the gate keeps a handle from being freed while the other uses it.
    private readonly Lock _gate = new();
    private readonly Func<IDisposable> _createClient;
    private readonly Func<IDisposable, IXbox360Controller> _createController;
    private IDisposable? _client;
    private volatile IXbox360Controller? _controller;

    public VigemBridge()
        : this(() => new ViGEmClient(), client => ((ViGEmClient)client).CreateXbox360Controller())
    {
    }

    internal VigemBridge(Func<IDisposable> createClient, Func<IDisposable, IXbox360Controller> createController)
    {
        _createClient = createClient;
        _createController = createController;
    }

    public bool IsConnected => _controller != null;

    // The XInput slot ViGEmBus assigned this virtual pad to, once connected - null when not
    // connected, or when connected but ViGEmBus hasn't reported the slot back yet (confirmed from
    // ViGEm.NET's own source: IXbox360Controller.UserIndex's getter throws
    // Xbox360UserIndexNotReportedException until that report arrives - it's set inside the same
    // feedback-notification callback used for rumble/LED state, not synchronously by Connect()).
    // This is queried every poll tick, so treating "not yet known" as an exception to catch here -
    // rather than letting it escape unguarded - is not optional.
    public int? UserIndex
    {
        get
        {
            lock (_gate)
            {
                if (_controller == null)
                    return null;

                try
                {
                    return _controller.UserIndex;
                }
                catch (Xbox360UserIndexNotReportedException)
                {
                    return null;
                }
            }
        }
    }

    public bool TryConnect()
    {
        if (_controller != null)
            return true;

        IDisposable? client = null;
        IXbox360Controller? controller = null;
        try
        {
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
