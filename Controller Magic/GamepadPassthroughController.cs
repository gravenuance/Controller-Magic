namespace ControllerMagic;

// Sits alongside ControllerPoller's existing read path (XInput/SDL -> PadState) rather than
// replacing it: when active, this additionally feeds the same per-tick PadState into a virtual
// Xbox 360 controller (via VigemBridge) while the real device is cloaked from everything else
// (via HidHideBridge) - so a game, Steam, or Windows' own UI only ever sees the virtual pad.
// That virtual pad structurally can never report the Guide button (PadButtons has no Guide bit
// to carry one), and its left stick/D-pad are kept neutral the whole time this is active, since
// that's also the whole time this app itself is using them for mouse/keyboard emulation - see
// VirtualPadReportMapper for why real values there would otherwise fight with Windows' built-in
// gamepad-driven UI focus navigation.
internal sealed class GamepadPassthroughController : IDisposable
{
    private readonly HidHideBridge _hidHide = new();
    private readonly VigemBridge _vigem = new();

    private volatile bool _fullscreenSuspended;
    // volatile bool, not a DriverStatus struct field: written from background tasks (the first-
    // tick probe, and Settings after a successful install) and read from the poll thread every
    // tick, so it needs to be a single primitive to be safely volatile.
    private volatile bool _driversReady;
    private bool _startupResetDone;
    private bool _lastAppliedActive;
    private int _transitioning;
    private PhysicalDeviceIdentity? _lastKnownDevice;

    // Pure policy, isolated from driver I/O so it's directly testable: the feature only ever
    // runs with the setting on, the drivers actually present, and no fullscreen exclusion zone
    // currently suppressing it.
    internal static bool ComputeShouldBeActive(bool settingOn, bool driversReady, bool fullscreenSuspended) =>
        settingOn && driversReady && !fullscreenSuspended;

    // Called once at construction (fire-and-forget, off the UI thread) and again from Settings
    // right after a successful driver install - never on a timer.
    public void RefreshDriverStatus()
    {
        var status = DriverDependency.Detect(_hidHide);
        _driversReady = status.HidHideInstalled && status.VigemInstalled;
    }

    // For Settings' toggle-enablement check: runs the same detection off the calling thread so a
    // UI-thread caller never blocks on it.
    public Task<DriverStatus> DetectDriverStatusAsync(CancellationToken ct = default) =>
        Task.Run(() => DriverDependency.Detect(_hidHide), ct);

    public void SetFullscreenSuspended(bool suspended) => _fullscreenSuspended = suspended;

    public void Tick(PadState pad, bool gotPad, PhysicalDeviceIdentity? deviceIdentity)
    {
        if (!_startupResetDone)
        {
            _startupResetDone = true;
            // Guaranteed crash/force-kill recovery: unconditionally clear cloaking left over from
            // a previous run that didn't exit cleanly, before evaluating current settings at all.
            _ = Task.Run(() =>
            {
                _hidHide.SetCloakingEnabled(false);
                RefreshDriverStatus();
            });
        }

        if (deviceIdentity.HasValue)
            _lastKnownDevice = deviceIdentity;

        bool wantActive = ComputeShouldBeActive(AppSettings.Instance.UseHidHide, _driversReady, _fullscreenSuspended)
            && gotPad && _lastKnownDevice.HasValue;

        if (wantActive != _lastAppliedActive && Interlocked.CompareExchange(ref _transitioning, 1, 0) == 0)
        {
            var device = _lastKnownDevice;
            _lastAppliedActive = wantActive;
            _ = Task.Run(() => ApplyTransition(wantActive, device)).ContinueWith(
                _ => Volatile.Write(ref _transitioning, 0),
                TaskScheduler.Default);
        }

        if (_lastAppliedActive && gotPad)
            _vigem.SubmitReport(pad, includeStickAndDpad: false);
    }

    private void ApplyTransition(bool active, PhysicalDeviceIdentity? device)
    {
        if (active)
        {
            _hidHide.EnsureAppAllowListed();
            if (device.HasValue)
                _hidHide.SetDeviceBlocked(device.Value.InterfacePath, true);
            _hidHide.SetCloakingEnabled(true);
            _vigem.TryConnect();
        }
        else
        {
            // Only the global cloak flag flips off here - the allow-list entry and the device's
            // block-list entry deliberately stay in place (per the "leave drivers/config
            // installed" decision) so re-activating is instant, whether that's the user flipping
            // the Settings toggle back on or a fullscreen exclusion ending.
            _vigem.Disconnect();
            _hidHide.SetCloakingEnabled(false);
        }
    }

    // Best-effort, called from ControllerPoller.Loop's finally block right before the poll
    // thread exits - covers both the ordinary clean-exit path (Stop() joins this thread) and a
    // same-process crash on the poll thread. A short synchronous wait here is a deliberate,
    // narrow exception to "never block on async": this is a genuine shutdown boundary, and the
    // guaranteed startup reset above is what actually keeps a missed shutdown from mattering.
    public void Shutdown()
    {
        try
        {
            _vigem.Disconnect();
            _hidHide.SetCloakingEnabled(false);
        }
        catch (Exception ex)
        {
            AppLog.Default.Warning("GamepadPassthroughController: error during shutdown cleanup", ex);
        }
    }

    public void Dispose()
    {
        Shutdown();
        _vigem.Dispose();
    }
}
