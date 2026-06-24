using System.Diagnostics;
using System.Reflection;
using Linearstar.Windows.RawInput;

namespace ControllerMagic;

internal sealed class RawInputPadReader
{
    private const int SonyVendorId = 0x054C;
    private const int DualShock4ProductIdGen1 = 0x05C4;
    private const int DualShock4ProductIdGen2 = 0x09CC;

    private readonly object _sync = new();

    private PadState _latest;
    private bool _hasState;
    private DateTime _lastUpdateUtc;

    private static long _packetCounter;
    private static DateTime _lastNoStateLogUtc;
    private static DateTime _lastConnectedLogUtc;

    public void Register(IntPtr hwnd)
    {
        //Debug.WriteLine($"[RAW] Register hwnd=0x{hwnd.ToInt64():X}");

        RawInputDevice.RegisterDevice(HidUsageAndPage.GamePad, RawInputDeviceFlags.ExInputSink, hwnd);
        RawInputDevice.RegisterDevice(HidUsageAndPage.Joystick, RawInputDeviceFlags.ExInputSink, hwnd);

        //Debug.WriteLine("[RAW] Register complete for GamePad + Joystick");
    }

    public bool TryGetLatest(out PadState state)
    {
        lock (_sync)
        {
            if (!_hasState)
            {
                if ((DateTime.UtcNow - _lastNoStateLogUtc).TotalSeconds >= 2)
                {
                    _lastNoStateLogUtc = DateTime.UtcNow;
                    //Debug.WriteLine("[RAW] TryGetLatest: no state available");
                }

                state = default;
                return false;
            }

            double ageSeconds = (DateTime.UtcNow - _lastUpdateUtc).TotalSeconds;
            if (ageSeconds > 2.0)
            {
                //Debug.WriteLine($"[RAW] TryGetLatest: state expired age={ageSeconds:F2}s");

                _latest = default;
                _hasState = false;
                state = default;
                return false;
            }

            state = _latest;

            if ((DateTime.UtcNow - _lastConnectedLogUtc).TotalSeconds >= 1)
            {
                _lastConnectedLogUtc = DateTime.UtcNow;
                //Debug.WriteLine(
                //    $"[RAW] TryGetLatest: connected buttons={state.Buttons} " +
                //    $"LX={state.LeftThumbX} LY={state.LeftThumbY} RX={state.RightThumbX} RY={state.RightThumbY} " +
                //    $"L2={state.LeftTrigger} R2={state.RightTrigger}");
            }

            return state.IsConnected;
        }
    }

    public void ProcessWindowMessage(IntPtr lParam)
    {
        long packetId = Interlocked.Increment(ref _packetCounter);
        RawInputData data;

        try
        {
            data = RawInputData.FromHandle(lParam);
        }
        catch (Exception ex)
        {
            //Debug.WriteLine($"[RAW] Packet {packetId}: FromHandle failed: {ex}");
            return;
        }

        //Debug.WriteLine($"[RAW] Packet {packetId}: dataType={data.GetType().FullName}");

        if (data is not RawInputHidData hid)
        {
            //Debug.WriteLine($"[RAW] Packet {packetId}: ignored non-HID input");
            return;
        }

        if (!TryTranslateHid(hid, out var state))
        {
            //Debug.WriteLine($"[RAW] Packet {packetId}: HID received but translation failed");
            return;
        }

        lock (_sync)
        {
            _latest = state;
            _hasState = true;
            _lastUpdateUtc = DateTime.UtcNow;
        }

        //Debug.WriteLine(
        //    $"[RAW] Packet {packetId}: state accepted buttons={state.Buttons} " +
        //    $"LX={state.LeftThumbX} LY={state.LeftThumbY} RX={state.RightThumbX} RY={state.RightThumbY} " +
        //    $"L2={state.LeftTrigger} R2={state.RightTrigger}");
    }

    private static bool TryTranslateHid(RawInputHidData hid, out PadState state)
    {
        state = default;

        var device = hid.Device;
        if (device is null)
        {
            //Debug.WriteLine("[RAW] TryTranslateHid: device is null");
            return false;
        }

        string devicePath = device.DevicePath ?? "<null>";
        //Debug.WriteLine($"[RAW] TryTranslateHid: path={devicePath}");

        if (!TryGetVendorProductId(device, out ushort vendorId, out ushort productId))
        {
            //Debug.WriteLine("[RAW] TryTranslateHid: VID/PID parse failed");
            return false;
        }

        //Debug.WriteLine($"[RAW] TryTranslateHid: VID=0x{vendorId:X4} PID=0x{productId:X4}");

        if (vendorId == SonyVendorId &&
            (productId == DualShock4ProductIdGen1 || productId == DualShock4ProductIdGen2))
        {
            bool ok = TryTranslateDualShock4(hid, out state);
            //Debug.WriteLine($"[RAW] TryTranslateHid: DS4 translate result={ok}");
            return ok;
        }

        //Debug.WriteLine("[RAW] TryTranslateHid: unsupported device");
        return false;
    }

    private static bool TryGetVendorProductId(RawInputDevice device, out ushort vendorId, out ushort productId)
    {
        vendorId = 0;
        productId = 0;

        string? devicePath = device.DevicePath;
        if (string.IsNullOrWhiteSpace(devicePath))
        {
            //Debug.WriteLine("[RAW] TryGetVendorProductId: empty device path");
            return false;
        }

        string upper = devicePath.ToUpperInvariant();

        // Style 1: standard USB HID path, e.g. VID_054C&PID_09CC
        if (TryParseTaggedHex(upper, "VID_", 4, out vendorId) &&
            TryParseTaggedHex(upper, "PID_", 4, out productId))
        {
            //Debug.WriteLine($"[RAW] TryGetVendorProductId: matched USB form VID=0x{vendorId:X4} PID=0x{productId:X4}");
            return true;
        }

        // Style 2: Bluetooth path, e.g. VID&0002054C_PID&09CC
        if (TryParseTaggedHexTail(upper, "VID&", 8, 4, out vendorId) &&
            TryParseTaggedHex(upper, "PID&", 4, out productId))
        {
            //Debug.WriteLine($"[RAW] TryGetVendorProductId: matched BT form VID=0x{vendorId:X4} PID=0x{productId:X4}");
            return true;
        }

        //Debug.WriteLine($"[RAW] TryGetVendorProductId: unsupported path format path={devicePath}");
        return false;
    }

    private static bool TryParseTaggedHex(string text, string tag, int hexDigits, out ushort value)
    {
        value = 0;

        int index = text.IndexOf(tag, StringComparison.Ordinal);
        if (index < 0)
            return false;

        int start = index + tag.Length;
        if (text.Length < start + hexDigits)
            return false;

        return ushort.TryParse(
            text.Substring(start, hexDigits),
            System.Globalization.NumberStyles.HexNumber,
            null,
            out value);
    }

    private static bool TryParseTaggedHexTail(string text, string tag, int totalHexDigits, int takeLastDigits, out ushort value)
    {
        value = 0;

        int index = text.IndexOf(tag, StringComparison.Ordinal);
        if (index < 0)
            return false;

        int start = index + tag.Length;
        if (text.Length < start + totalHexDigits)
            return false;

        string raw = text.Substring(start, totalHexDigits);
        if (raw.Length < takeLastDigits)
            return false;

        string tail = raw.Substring(raw.Length - takeLastDigits, takeLastDigits);

        return ushort.TryParse(
            tail,
            System.Globalization.NumberStyles.HexNumber,
            null,
            out value);
    }

    private static bool TryTranslateDualShock4(RawInputHidData hid, out PadState state)
    {
        state = default;

        if (!TryExtractReportBytes(hid.Hid, out var report))
        {
            //Debug.WriteLine("[RAW] TryTranslateDualShock4: report extraction failed");
            return false;
        }

        //Debug.WriteLine($"[RAW] TryTranslateDualShock4: reportLen={report.Length} bytes={BitConverter.ToString(report, 0, Math.Min(report.Length, 16))}");

        if (report.Length < 8)
        {
            //Debug.WriteLine("[RAW] TryTranslateDualShock4: report too short");
            return false;
        }

        int offset = GetDs4ReportOffset(report);
        //Debug.WriteLine($"[RAW] TryTranslateDualShock4: offset={offset}");

        if (report.Length < offset + 8)
        {
            //Debug.WriteLine("[RAW] TryTranslateDualShock4: report too short after offset");
            return false;
        }

        byte lx = report[offset + 0];
        byte ly = report[offset + 1];
        byte rx = report[offset + 2];
        byte ry = report[offset + 3];
        byte b4 = report[offset + 4];
        byte b5 = report[offset + 5];
        byte l2 = report[offset + 7];
        byte r2 = report[offset + 8];

        if ((b4 & 0x0F) > 8)
        {
            //Debug.WriteLine($"[RAW] TryTranslateDualShock4: invalid dpad nibble b4=0x{b4:X2} offset={offset}");
            return false;
        }

        PadButtons buttons = PadButtons.None;

        if ((b4 & 0x20) != 0) buttons |= PadButtons.A;
        if ((b4 & 0x40) != 0) buttons |= PadButtons.B;
        if ((b4 & 0x10) != 0) buttons |= PadButtons.X;
        if ((b4 & 0x80) != 0) buttons |= PadButtons.Y;

        if ((b5 & 0x01) != 0) buttons |= PadButtons.LeftShoulder;
        if ((b5 & 0x02) != 0) buttons |= PadButtons.RightShoulder;
        if ((b5 & 0x10) != 0) buttons |= PadButtons.Back;
        if ((b5 & 0x20) != 0) buttons |= PadButtons.Start;
        if ((b5 & 0x40) != 0) buttons |= PadButtons.LeftThumb;
        if ((b5 & 0x80) != 0) buttons |= PadButtons.RightThumb;

        int dpadNibble = b4 & 0x0F;
        if (dpadNibble < 0 || dpadNibble > 8)
        {
            //Debug.WriteLine($"[RAW] TryTranslateDualShock4: clamping invalid dpad nibble b4=0x{b4:X2} to neutral");
            dpadNibble = 8;
        }

        MapDs4Dpad(dpadNibble, ref buttons);

        state = new PadState
        {
            IsConnected = true,
            LeftThumbX = ConvertByteAxis(lx),
            LeftThumbY = InvertAxis(ConvertByteAxis(ly)),
            RightThumbX = ConvertByteAxis(rx),
            RightThumbY = InvertAxis(ConvertByteAxis(ry)),
            LeftTrigger = l2,
            RightTrigger = r2,
            Buttons = buttons
        };

        Debug.WriteLine(
        "[RAW] DS4 bytes " +
        string.Join(" ", report.Take(32).Select((b, i) => $"{i}:{b:X2}")));

        //Debug.WriteLine($"[RAW] TryTranslateDualShock4: buttons={buttons} raw=({lx},{ly},{rx},{ry},{l2},{r2})");
        return true;
    }

    private static bool TryExtractReportBytes(object hidPayload, out byte[] report)
    {
        report = Array.Empty<byte>();

        if (hidPayload is null)
        {
            //Debug.WriteLine("[RAW] TryExtractReportBytes: payload is null");
            return false;
        }

        if (hidPayload is byte[] bytes)
        {
           // Debug.WriteLine($"[RAW] TryExtractReportBytes: payload is byte[] len={bytes.Length}");
            report = bytes;
            return report.Length > 0;
        }

        var type = hidPayload.GetType();
        //Debug.WriteLine($"[RAW] TryExtractReportBytes: payloadType={type.FullName}");

        foreach (string propertyName in new[] { "RawData", "Data", "Bytes" })
        {
            PropertyInfo? property = type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
            if (property?.GetValue(hidPayload) is byte[] propertyBytes && propertyBytes.Length > 0)
            {
                //Debug.WriteLine($"[RAW] TryExtractReportBytes: matched property {propertyName} len={propertyBytes.Length}");
                report = propertyBytes;
                return true;
            }
        }

        foreach (string fieldName in new[] { "RawData", "Data", "Bytes" })
        {
            FieldInfo? field = type.GetField(fieldName, BindingFlags.Public | BindingFlags.Instance);
            if (field?.GetValue(hidPayload) is byte[] fieldBytes && fieldBytes.Length > 0)
            {
                //Debug.WriteLine($"[RAW] TryExtractReportBytes: matched field {fieldName} len={fieldBytes.Length}");
                report = fieldBytes;
                return true;
            }
        }

        //Debug.WriteLine("[RAW] TryExtractReportBytes: no supported byte source found");
        return false;
    }

    private static int GetDs4ReportOffset(byte[] report)
    {
        if (report is null || report.Length < 8)
            return 0;

        // DS4 USB input report:
        // 0: report id (0x01), 1.. = LX, LY, RX, RY, buttons...
        if (report[0] == 0x01)
            return 1;

        // DS4 Bluetooth packets seen in your logs start like:
        // 11-C0-00-80-80-80-80-08...
        // The actual pad payload begins at byte 3.
        if (report.Length >= 11 &&
            report[0] == 0x11 &&
            report[1] == 0xC0)
        {
            return 3;
        }

        return 0;
    }

    private static void MapDs4Dpad(int dpadValue, ref PadButtons buttons)
    {
        switch (dpadValue)
        {
            case 0: buttons |= PadButtons.DPadUp; break;
            case 1: buttons |= PadButtons.DPadUp | PadButtons.DPadRight; break;
            case 2: buttons |= PadButtons.DPadRight; break;
            case 3: buttons |= PadButtons.DPadRight | PadButtons.DPadDown; break;
            case 4: buttons |= PadButtons.DPadDown; break;
            case 5: buttons |= PadButtons.DPadDown | PadButtons.DPadLeft; break;
            case 6: buttons |= PadButtons.DPadLeft; break;
            case 7: buttons |= PadButtons.DPadLeft | PadButtons.DPadUp; break;
            case 8:
            default:
                break;
        }
    }

    private static short ConvertByteAxis(byte value)
    {
        int centered = value - 128;
        int scaled = centered * 256;

        if (scaled < short.MinValue) scaled = short.MinValue;
        if (scaled > short.MaxValue) scaled = short.MaxValue;

        return (short)scaled;
    }

    private static short InvertAxis(short value)
    {
        if (value == short.MinValue)
            return short.MaxValue;

        return (short)-value;
    }
}