using System.Runtime.InteropServices;

namespace ControllerMagic;

internal readonly record struct SoundOutput(string Id, string Name);

internal enum CycleDirection
{
    Next,
    Previous,
}

internal interface ISoundOutputs
{
    // Enabled and connected playback devices, in any order.
    IReadOnlyList<SoundOutput> Active();

    // Null when Windows has no default playback device.
    string? DefaultId();

    void SetDefault(string id);
}

// Steps Windows' default playback device through the connected ones, in name order so the cycle is predictable.
internal sealed class SoundOutputCycler(ISoundOutputs outputs)
{
    // Two quick presses each step from the device the other one chose, not both from the same start.
    private readonly Lock _gate = new();

    // The output now in use, or null when there's none or Windows refused the change.
    public SoundOutput? Cycle(CycleDirection direction)
    {
        lock (_gate)
        {
            try
            {
                var active = outputs.Active().OrderBy(o => o.Name, StringComparer.OrdinalIgnoreCase).ToList();
                if (active.Count == 0)
                {
                    AppLog.Default.Warning("SoundOutputCycler: no connected sound outputs to switch between");
                    return null;
                }

                string? current = outputs.DefaultId();
                int index = active.FindIndex(o => o.Id == current);
                int step = direction switch
                {
                    CycleDirection.Next => 1,
                    CycleDirection.Previous => -1,
                    _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, null),
                };
                int chosen = index < 0
                    ? (step > 0 ? 0 : active.Count - 1)
                    : (index + step + active.Count) % active.Count;

                var output = active[chosen];
                if (output.Id != current)
                {
                    outputs.SetDefault(output.Id);
                    AppLog.Default.Info($"SoundOutputCycler: switched sound output to '{output.Name}'");
                }

                return output;
            }
            // Interop turns a failing HRESULT into one of these, not always a COMException.
            catch (Exception ex) when (ex is ExternalException or UnauthorizedAccessException or InvalidCastException or ArgumentException)
            {
                AppLog.Default.Warning($"SoundOutputCycler: couldn't switch sound output (HRESULT 0x{ex.HResult:X8})", ex);
                return null;
            }
        }
    }
}

// Core Audio lists the devices; setting the default goes through IPolicyConfig, which Windows' own
// Sound settings use. It is undocumented but unchanged since Windows 7.
internal sealed class CoreAudioSoundOutputs : ISoundOutputs
{
    private const int eRender = 0;
    private const int DEVICE_STATE_ACTIVE = 0x1;
    private const int STGM_READ = 0;
    private const ushort VT_LPWSTR = 31;
    private const int E_NOTFOUND = unchecked((int)0x80070490);

    private enum ERole
    {
        Console = 0,
        Multimedia = 1,
        Communications = 2,
    }

    private static readonly PROPERTYKEY FriendlyNameKey = new(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 14);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct PROPERTYKEY(Guid formatId, int propertyId)
    {
        public readonly Guid FormatId = formatId;
        public readonly int PropertyId = propertyId;
    }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct PROPVARIANT
    {
        [FieldOffset(0)]
        public ushort vt;

        [FieldOffset(8)]
        public nint pointer;
    }

    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumerator;

    [ComImport]
    [Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
    private class PolicyConfigClient;

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        IMMDeviceCollection EnumAudioEndpoints(int dataFlow, int stateMask);

        [PreserveSig]
        int GetDefaultAudioEndpoint(int dataFlow, ERole role, out IMMDevice? endpoint);
    }

    [ComImport]
    [Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    {
        int GetCount();

        IMMDevice Item(int index);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig]
        int Activate();

        IPropertyStore OpenPropertyStore(int access);

        [return: MarshalAs(UnmanagedType.LPWStr)]
        string GetId();
    }

    [ComImport]
    [Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig]
        int GetCount();

        [PreserveSig]
        int GetAt();

        void GetValue(in PROPERTYKEY key, out PROPVARIANT value);
    }

    // Only SetDefaultEndpoint is called; the slots before it keep the vtable in order.
    [ComImport]
    [Guid("f8679f50-850a-41cf-9c72-430f290290c8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat();
        [PreserveSig] int GetDeviceFormat();
        [PreserveSig] int ResetDeviceFormat();
        [PreserveSig] int SetDeviceFormat();
        [PreserveSig] int GetProcessingPeriod();
        [PreserveSig] int SetProcessingPeriod();
        [PreserveSig] int GetShareMode();
        [PreserveSig] int SetShareMode();
        [PreserveSig] int GetPropertyValue();
        [PreserveSig] int SetPropertyValue();

        void SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, ERole role);
    }

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PROPVARIANT value);

    public IReadOnlyList<SoundOutput> Active()
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        var devices = enumerator.EnumAudioEndpoints(eRender, DEVICE_STATE_ACTIVE);
        int count = devices.GetCount();
        var outputs = new List<SoundOutput>(count);
        for (int i = 0; i < count; i++)
        {
            var device = devices.Item(i);
            string id = device.GetId();
            outputs.Add(new SoundOutput(id, FriendlyName(device) ?? id));
        }

        return outputs;
    }

    public string? DefaultId()
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        int hr = enumerator.GetDefaultAudioEndpoint(eRender, ERole.Multimedia, out var device);
        if (hr == E_NOTFOUND)
            return null;
        Marshal.ThrowExceptionForHR(hr);
        return device?.GetId();
    }

    // Every role, as picking a device in Windows' sound settings does, so calls follow too.
    public void SetDefault(string id)
    {
        var policy = (IPolicyConfig)new PolicyConfigClient();
        policy.SetDefaultEndpoint(id, ERole.Console);
        policy.SetDefaultEndpoint(id, ERole.Multimedia);
        policy.SetDefaultEndpoint(id, ERole.Communications);
    }

    private static string? FriendlyName(IMMDevice device)
    {
        var store = device.OpenPropertyStore(STGM_READ);
        store.GetValue(FriendlyNameKey, out var value);
        try
        {
            return value.vt == VT_LPWSTR ? Marshal.PtrToStringUni(value.pointer) : null;
        }
        finally
        {
            _ = PropVariantClear(ref value);
        }
    }
}
