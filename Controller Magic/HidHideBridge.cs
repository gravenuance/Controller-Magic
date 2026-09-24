using Nefarius.Drivers.HidHide;
using Nefarius.Drivers.HidHide.Exceptions;
using Nefarius.Utilities.DeviceManagement.PnP;

namespace ControllerMagic;

internal enum BlockResult
{
    AlreadyBlocked,
    NewlyBlocked,
    Failed,
}

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
    public BlockResult BlockDevice(string deviceInterfacePath)
    {
        try
        {
            var instanceIds = ResolveInstanceIds(deviceInterfacePath);
            if (instanceIds.Count == 0)
            {
                AppLog.Default.Warning($"HidHideBridge: could not resolve an instance id for {deviceInterfacePath}");
                return BlockResult.Failed;
            }

            var blockedIds = _service.BlockedInstanceIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var newIds = instanceIds.Where(id => !blockedIds.Contains(id)).ToList();
            foreach (string instanceId in newIds)
                _service.AddBlockedInstanceId(instanceId);

            return newIds.Count == 0 ? BlockResult.AlreadyBlocked : BlockResult.NewlyBlocked;
        }
        catch (Exception ex)
        {
            AppLog.Default.Warning($"HidHideBridge: failed to block {deviceInterfacePath}", ex);
            return BlockResult.Failed;
        }
    }

    private static List<string> ResolveInstanceIds(string deviceInterfacePath)
    {
        if (PhysicalDeviceIdentity.IsXInputPlaceholderPath(deviceInterfacePath))
            return FindPhysicalXInputInstanceIds();

        string? instanceId = PnPDevice.GetInstanceIdFromInterfaceId(deviceInterfacePath);
        return string.IsNullOrEmpty(instanceId) ? [] : [instanceId];
    }

    // XInput can't map a slot to a device, so every physical XInput pad is hidden; IsVirtual keeps
    // ViGEmBus's own virtual pad visible.
    private static List<string> FindPhysicalXInputInstanceIds()
    {
        var instanceIds = new List<string>();
        AddPhysicalDevices(DeviceInterfaceIds.XUsbDevice, _ => true, instanceIds);
        AddPhysicalDevices(DeviceInterfaceIds.HidDevice, id => id.Contains("IG_", StringComparison.OrdinalIgnoreCase), instanceIds);
        return instanceIds;
    }

    private static void AddPhysicalDevices(Guid interfaceGuid, Func<string, bool> include, List<string> instanceIds)
    {
        for (int i = 0; Devcon.FindByInterfaceGuid(interfaceGuid, out PnPDevice device, i); i++)
        {
            if (include(device.InstanceId) && !device.IsVirtual())
                instanceIds.Add(device.InstanceId);
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
        catch (HidHideDriverNotFoundException)
        {
            // Expected, not warning-worthy: GamepadPassthroughController.ForceOffAtStartup calls
            // this unconditionally on every launch, for every user, regardless of whether HidHide
            // is even installed - which it isn't for the overwhelming majority who've never
            // turned "Use HidHide" on. There's nothing to toggle, so nothing went wrong.
        }
        catch (Exception ex)
        {
            AppLog.Default.Warning("HidHideBridge: failed to toggle cloaking", ex);
        }
    }
}
