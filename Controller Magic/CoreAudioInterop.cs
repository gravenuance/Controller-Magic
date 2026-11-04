using System.Runtime.InteropServices;

namespace ControllerMagic;

// The Core Audio COM surface the sound-output and volume code share. Only the members called are
// typed out; the placeholder slots before them keep each vtable in order.
internal static class CoreAudioInterop
{
    internal const int eRender = 0;
    internal const int DEVICE_STATE_ACTIVE = 0x1;
    internal const int CLSCTX_ALL = 0x17;
    internal const int E_NOTFOUND = unchecked((int)0x80070490);

    internal enum ERole
    {
        Console = 0,
        Multimedia = 1,
        Communications = 2,
    }

    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct PROPERTYKEY(Guid formatId, int propertyId)
    {
        public readonly Guid FormatId = formatId;
        public readonly int PropertyId = propertyId;
    }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    internal struct PROPVARIANT
    {
        [FieldOffset(0)]
        public ushort vt;

        [FieldOffset(8)]
        public nint pointer;
    }

    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    internal class MMDeviceEnumerator;

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDeviceEnumerator
    {
        IMMDeviceCollection EnumAudioEndpoints(int dataFlow, int stateMask);

        [PreserveSig]
        int GetDefaultAudioEndpoint(int dataFlow, ERole role, out IMMDevice? endpoint);
    }

    [ComImport]
    [Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDeviceCollection
    {
        int GetCount();

        IMMDevice Item(int index);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDevice
    {
        [return: MarshalAs(UnmanagedType.IUnknown)]
        object Activate(in Guid iid, int clsCtx, nint activationParams);

        IPropertyStore OpenPropertyStore(int access);

        [return: MarshalAs(UnmanagedType.LPWStr)]
        string GetId();
    }

    [ComImport]
    [Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPropertyStore
    {
        [PreserveSig]
        int GetCount();

        [PreserveSig]
        int GetAt();

        void GetValue(in PROPERTYKEY key, out PROPVARIANT value);
    }

    [ComImport]
    [Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify();
        [PreserveSig] int UnregisterControlChangeNotify();
        [PreserveSig] int GetChannelCount();
        [PreserveSig] int SetMasterVolumeLevel();
        [PreserveSig] int SetMasterVolumeLevelScalar();
        [PreserveSig] int GetMasterVolumeLevel();

        float GetMasterVolumeLevelScalar();

        [PreserveSig] int SetChannelVolumeLevel();
        [PreserveSig] int SetChannelVolumeLevelScalar();
        [PreserveSig] int GetChannelVolumeLevel();
        [PreserveSig] int GetChannelVolumeLevelScalar();

        void SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, in Guid eventContext);

        [return: MarshalAs(UnmanagedType.Bool)]
        bool GetMute();

        [PreserveSig] int GetVolumeStepInfo();

        void VolumeStepUp(in Guid eventContext);

        void VolumeStepDown(in Guid eventContext);
    }

    // Null when Windows has no default playback device.
    internal static IMMDevice? DefaultRenderDevice()
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        try
        {
            int hr = enumerator.GetDefaultAudioEndpoint(eRender, ERole.Multimedia, out var device);
            if (hr == E_NOTFOUND)
                return null;
            Marshal.ThrowExceptionForHR(hr);
            return device;
        }
        finally
        {
            _ = Marshal.ReleaseComObject(enumerator);
        }
    }
}
