using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Exceptions;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;
using Nefarius.ViGEm.Client.Targets.Xbox360.Exceptions;

namespace ControllerMagic;

// Owns one virtual Xbox 360 controller for the lifetime of the "suppress Guide button" mode.
// AutoSubmitReport is turned off so a whole PadState lands as one USB report instead of one
// report per SetButtonState/SetAxisValue call.
// The virtual pad operations passthrough needs, so its state machine can run against a fake in tests.
internal interface IVirtualPad : IDisposable
{
    bool IsConnected { get; }
    int? UserIndex { get; }
    bool TryConnect();
    void SubmitReport(PadState pad, bool includeStickAndDpad = true);
    void Disconnect();
}

internal sealed class VigemBridge : IVirtualPad
{
    private ViGEmClient? _client;
    private IXbox360Controller? _controller;

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
            var controller = _controller;
            if (controller == null)
                return null;

            try
            {
                return controller.UserIndex;
            }
            catch (Xbox360UserIndexNotReportedException)
            {
                return null;
            }
        }
    }

    public bool TryConnect()
    {
        if (_controller != null)
            return true;

        try
        {
            var client = new ViGEmClient();
            var controller = client.CreateXbox360Controller();
            controller.AutoSubmitReport = false;
            controller.Connect();

            _client = client;
            _controller = controller;
            return true;
        }
        catch (VigemBusNotFoundException ex)
        {
            AppLog.Default.Warning("VigemBridge: ViGEmBus driver not found", ex);
            Cleanup();
            return false;
        }
        catch (Exception ex)
        {
            AppLog.Default.Warning("VigemBridge: failed to connect a virtual controller", ex);
            Cleanup();
            return false;
        }
    }

    // includeStickAndDpad=false keeps the left stick and D-pad neutral on the virtual pad while
    // this app's own stick-as-mouse control is active, so Windows' gamepad-driven UI focus
    // navigation (see VirtualPadReportMapper) doesn't fight with it. The right stick and triggers
    // are unaffected - only the D-pad and left stick drive that Windows feature.
    public void SubmitReport(PadState pad, bool includeStickAndDpad = true)
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
        }
        catch (Exception ex)
        {
            AppLog.Default.Warning("VigemBridge: failed to submit a report; disconnecting", ex);
            Cleanup();
        }
    }

    public void Disconnect() => Cleanup();

    private void Cleanup()
    {
        try
        {
            _controller?.Disconnect();
        }
        catch (Exception ex)
        {
            AppLog.Default.Warning("VigemBridge: error disconnecting the virtual controller", ex);
        }

        _controller = null;

        try
        {
            _client?.Dispose();
        }
        catch (Exception ex)
        {
            AppLog.Default.Warning("VigemBridge: error disposing the ViGEm client", ex);
        }

        _client = null;
    }

    public void Dispose() => Cleanup();
}
