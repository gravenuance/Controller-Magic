using System.Net.NetworkInformation;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Exceptions;

namespace ControllerMagic;

internal readonly record struct DriverStatus(bool HidHideInstalled, bool VigemInstalled, bool NetworkAvailable);

internal static class DriverDependency
{
    // Isolated from the I/O checks that produce DriverStatus so it's a plain, directly testable
    // function - the toggle is usable offline once both drivers are already installed, or while
    // they aren't yet as long as a first-time install could be attempted.
    internal static bool ShouldToggleBeEnabled(DriverStatus status) =>
        (status.HidHideInstalled && status.VigemInstalled) || status.NetworkAvailable;

    public static DriverStatus Detect(IHidHide hidHide) => new(
        HidHideInstalled: hidHide.IsInstalled,
        VigemInstalled: IsVigemBusInstalled(),
        NetworkAvailable: IsNetworkAvailable());

    // Constructing a client is ViGEmBus's own documented way to probe for the driver - it throws
    // VigemBusNotFoundException immediately when the bus isn't present, with no service name to
    // guess and no extra dependency on System.ServiceProcess.
    private static bool IsVigemBusInstalled()
    {
        try
        {
            using var client = new ViGEmClient();
            return true;
        }
        catch (VigemBusNotFoundException)
        {
            return false;
        }
        catch (Exception ex)
        {
            AppLog.Default.Warning("DriverDependency: failed to probe the ViGEmBus driver", ex);
            return false;
        }
    }

    private static bool IsNetworkAvailable()
    {
        try
        {
            return NetworkInterface.GetIsNetworkAvailable();
        }
        catch (Exception ex)
        {
            AppLog.Default.Warning("DriverDependency: failed to query network availability", ex);
            return false;
        }
    }
}
