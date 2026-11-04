using System.Runtime.InteropServices;
using static ControllerMagic.CoreAudioInterop;

namespace ControllerMagic;

// Percent is 0..100, as Windows' own volume flyout shows it.
internal readonly record struct VolumeLevel(int Percent, bool Muted);

internal interface ISystemVolume
{
    // The default playback device as it is now; one step works on one device throughout.
    IVolumeEndpoint Open();
}

internal interface IVolumeEndpoint : IDisposable
{
    void Step(VolumeStep step);

    bool IsMuted();

    void SetMuted(bool muted);

    // 0..1.
    float Level();
}

// Steps the system volume the way the keyboard volume keys do, including unmuting on a step up,
// and reports the result so it can be shown.
internal sealed class VolumeStepper(ISystemVolume volume)
{
    // Held-down repeats run as separate tasks; each must step from where the last one left off,
    // and report in that same order, or the cue could end on an older level.
    private readonly Lock _gate = new();

    // report gets the new level, or null when there's no playback device or Windows refused the change.
    public void Step(VolumeStep step, Action<VolumeLevel?> report)
    {
        ArgumentNullException.ThrowIfNull(report);
        lock (_gate)
            report(TryStep(step));
    }

    private VolumeLevel? TryStep(VolumeStep step)
    {
        try
        {
            using var endpoint = volume.Open();
            if (step == VolumeStep.Up && endpoint.IsMuted())
                endpoint.SetMuted(false);

            endpoint.Step(step);
            return new VolumeLevel((int)MathF.Round(endpoint.Level() * 100f), endpoint.IsMuted());
        }
        // Interop turns a failing HRESULT into one of these, not always a COMException.
        catch (Exception ex) when (ex is ExternalException or UnauthorizedAccessException or InvalidCastException or ArgumentException or InvalidOperationException)
        {
            AppLog.Default.Warning($"VolumeStepper: couldn't change the volume (HRESULT 0x{ex.HResult:X8})", ex);
            return null;
        }
    }
}

// IAudioEndpointVolume on the default playback device, which the volume keys also change. Its
// step size is Windows' own, so a step here matches a key press.
internal sealed class CoreAudioVolume : ISystemVolume
{
    private static readonly Guid AudioEndpointVolumeId = typeof(IAudioEndpointVolume).GUID;

    public IVolumeEndpoint Open()
    {
        var device = DefaultRenderDevice() ?? throw new InvalidOperationException("no default playback device");
        try
        {
            return new Endpoint((IAudioEndpointVolume)device.Activate(AudioEndpointVolumeId, CLSCTX_ALL, 0));
        }
        finally
        {
            _ = Marshal.ReleaseComObject(device);
        }
    }

    // Released when done rather than left to the finalizer: a held step opens ten a second.
    private sealed class Endpoint(IAudioEndpointVolume volume) : IVolumeEndpoint
    {
        public void Step(VolumeStep step)
        {
            switch (step)
            {
                case VolumeStep.Up:
                    volume.VolumeStepUp(Guid.Empty);
                    break;
                case VolumeStep.Down:
                    volume.VolumeStepDown(Guid.Empty);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(step), step, null);
            }
        }

        public bool IsMuted() => volume.GetMute();

        public void SetMuted(bool muted) => volume.SetMute(muted, Guid.Empty);

        public float Level() => volume.GetMasterVolumeLevelScalar();

        public void Dispose() => _ = Marshal.ReleaseComObject(volume);
    }
}
