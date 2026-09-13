using Nefarius.Drivers.HidHide;
using Nefarius.Utilities.DeviceManagement.PnP;

namespace ControllerMagic;

// Thin wrapper around HidHide's control service: cloaks the physical controller from every
// process except this app, so Xbox Game Bar and Steam never see a Guide-button press on it at
// all - there's no config flag either exposes to disable that, so this is the only reliable fix.
internal sealed class HidHideBridge
{
    private readonly HidHideControlService _service = new();

    public bool IsInstalled
    {
        get
        {
            try
            {
                return _service.IsInstalled;
            }
            catch (Exception ex)
            {
                AppLog.Default.Warning("HidHideBridge: failed to query installation state", ex);
                return false;
            }
        }
    }

    public void EnsureAppAllowListed()
    {
        try
        {
            string exePath = System.Windows.Forms.Application.ExecutablePath;
            if (!_service.ApplicationPaths.Contains(exePath, StringComparer.OrdinalIgnoreCase))
                _service.AddApplicationPath(exePath, throwIfInvalid: false);
        }
        catch (Exception ex)
        {
            AppLog.Default.Warning("HidHideBridge: failed to allow-list this app", ex);
        }
    }

    // deviceInterfacePath is SDL's device *interface* path (SDL_JoystickPath's format); HidHide's
    // block-list keys on the device *instance* path instead, so it's converted here rather than
    // asking every caller to know about that distinction.
    public void SetDeviceBlocked(string deviceInterfacePath, bool blocked)
    {
        try
        {
            string? instanceId = PnPDevice.GetInstanceIdFromInterfaceId(deviceInterfacePath);
            if (string.IsNullOrEmpty(instanceId))
            {
                AppLog.Default.Warning($"HidHideBridge: could not resolve an instance id for {deviceInterfacePath}");
                return;
            }

            bool alreadyBlocked = _service.BlockedInstanceIds.Contains(instanceId, StringComparer.OrdinalIgnoreCase);
            if (blocked && !alreadyBlocked)
                _service.AddBlockedInstanceId(instanceId);
            else if (!blocked && alreadyBlocked)
                _service.RemoveBlockedInstanceId(instanceId);
        }
        catch (Exception ex)
        {
            AppLog.Default.Warning($"HidHideBridge: failed to update block state for {deviceInterfacePath}", ex);
        }
    }

    // The one flag that matters for both clean-exit and crash recovery: turning this off restores
    // normal access to every cloaked device immediately, regardless of what's in the block list.
    public void SetCloakingEnabled(bool enabled)
    {
        try
        {
            _service.IsActive = enabled;
        }
        catch (Exception ex)
        {
            AppLog.Default.Warning("HidHideBridge: failed to toggle cloaking", ex);
        }
    }
}
