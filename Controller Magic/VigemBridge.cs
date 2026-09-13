using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Exceptions;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;

namespace ControllerMagic;

// Owns one virtual Xbox 360 controller for the lifetime of the "suppress Guide button" mode.
// AutoSubmitReport is turned off so a whole PadState lands as one USB report instead of one
// report per SetButtonState/SetAxisValue call.
internal sealed class VigemBridge : IDisposable
{
    private ViGEmClient? _client;
    private IXbox360Controller? _controller;

    public bool IsConnected => _controller != null;

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
