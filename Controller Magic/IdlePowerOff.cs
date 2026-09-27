using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;

namespace ControllerMagic;

internal interface IControllerPowerOff
{
    // Drops the pad's Bluetooth link, which switches a wireless pad off. False if nothing was disconnected.
    bool TryPowerOff(ulong bluetoothAddress);
}

// What counts as using the pad, in the units the poller already works in.
internal readonly record struct IdleThresholds(int StickDeadZone, int ScrollDeadZone);

// Switches a wireless pad off after it goes unused, as Steam would if it could see it: with HidHide
// hiding the pad, Steam's own "turn off when idle" never fires.
internal sealed class IdlePowerOff(TimeProvider clock, IControllerPowerOff power)
{
    // Past this a trigger is being pressed, not resting.
    private const byte TriggerThreshold = 30;

    private long? _lastActive;
    private bool _poweredOff;

    // Called every tick with a connected pad; powers it off once when it has been idle for the timeout.
    public void Observe(PadState pad, ulong? bluetoothAddress, IdleThresholds thresholds, TimeSpan timeout)
    {
        long now = clock.GetTimestamp();
        if (_lastActive is null || IsActive(pad, thresholds))
        {
            _lastActive = now;
            _poweredOff = false;
            return;
        }

        if (timeout <= TimeSpan.Zero || _poweredOff || bluetoothAddress is not { } address
            || clock.GetElapsedTime(_lastActive.Value, now) < timeout)
            return;

        _poweredOff = true;
        if (power.TryPowerOff(address))
            AppLog.Default.Info($"IdlePowerOff: switched the controller off after {timeout.TotalMinutes:0} min unused");
    }

    // The next observation starts a fresh wait: a new pad, or one a fullscreen game was using.
    public void Reset()
    {
        _lastActive = null;
        _poweredOff = false;
    }

    internal static bool IsActive(PadState pad, IdleThresholds thresholds) =>
        pad.Buttons != PadButtons.None
        || pad.TouchActive
        || pad.LeftTrigger > TriggerThreshold
        || pad.RightTrigger > TriggerThreshold
        || !ControllerPoller.IsInsideDeadZone(ControllerPoller.StickMagnitude(pad.LeftThumbX, pad.LeftThumbY), thresholds.StickDeadZone)
        || !ControllerPoller.IsInsideDeadZone(ControllerPoller.StickMagnitude(pad.RightThumbX, pad.RightThumbY), thresholds.ScrollDeadZone);

    // SDL reports a PlayStation pad's Bluetooth address as its serial, with or without separators.
    internal static ulong? ParseBluetoothAddress(string? serial)
    {
        if (string.IsNullOrEmpty(serial))
            return null;

        string hex = serial.Replace(":", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal);
        return hex.Length == 12
            && ulong.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ulong address)
            && address != 0
                ? address
                : null;
    }
}

// Needs no elevation: a standard user may disconnect a device from the radio it's connected to.
internal sealed class BluetoothPowerOff : IControllerPowerOff
{
    private const uint IOCTL_BTH_DISCONNECT_DEVICE = 0x41000C;

    [StructLayout(LayoutKind.Sequential)]
    private struct BLUETOOTH_FIND_RADIO_PARAMS
    {
        public uint dwSize;
    }

    [DllImport("BluetoothApis.dll", SetLastError = true)]
    private static extern IntPtr BluetoothFindFirstRadio(ref BLUETOOTH_FIND_RADIO_PARAMS pbtfrp, out IntPtr phRadio);

    [DllImport("BluetoothApis.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BluetoothFindNextRadio(IntPtr hFind, out IntPtr phRadio);

    [DllImport("BluetoothApis.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BluetoothFindRadioClose(IntPtr hFind);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(
        IntPtr hDevice, uint dwIoControlCode, ref ulong lpInBuffer, int nInBufferSize,
        IntPtr lpOutBuffer, int nOutBufferSize, out int lpBytesReturned, IntPtr lpOverlapped);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    // Tries every radio, since the pad is only known to the one it's connected through.
    public bool TryPowerOff(ulong bluetoothAddress)
    {
        var findParams = new BLUETOOTH_FIND_RADIO_PARAMS { dwSize = (uint)Marshal.SizeOf<BLUETOOTH_FIND_RADIO_PARAMS>() };
        IntPtr find = BluetoothFindFirstRadio(ref findParams, out IntPtr radio);
        if (find == IntPtr.Zero)
        {
            AppLog.Default.Warning($"IdlePowerOff: no Bluetooth radio to switch the controller off with ({new Win32Exception(Marshal.GetLastPInvokeError()).Message})");
            return false;
        }

        int lastError = 0;
        try
        {
            do
            {
                ulong address = bluetoothAddress;
                bool disconnected = DeviceIoControl(radio, IOCTL_BTH_DISCONNECT_DEVICE, ref address, sizeof(ulong), IntPtr.Zero, 0, out _, IntPtr.Zero);
                if (!disconnected)
                    lastError = Marshal.GetLastPInvokeError();
                CloseHandle(radio);
                if (disconnected)
                    return true;
            }
            while (BluetoothFindNextRadio(find, out radio));
        }
        finally
        {
            BluetoothFindRadioClose(find);
        }

        // A pad on USB has no Bluetooth link to drop; that's expected, not a fault.
        AppLog.Default.Info($"IdlePowerOff: couldn't switch the controller off ({new Win32Exception(lastError).Message}); it may be on USB");
        return false;
    }
}
