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
    private readonly TimeProvider _clock;

    private volatile bool _fullscreenSuspended;
    // volatile bool, not a DriverStatus struct field: written from background tasks (the first-
    // tick probe, and Settings after a successful install) and read from the poll thread every
    // tick, so it needs to be a single primitive to be safely volatile.
    private volatile bool _driversReady;
    private bool _startupResetDone;
    private bool _lastAppliedActive;
    private int _transitioning;
    private PhysicalDeviceIdentity? _lastKnownDevice;

    // Safety cutoff, kept as defense-in-depth even after the leak below was root-caused and
    // fixed: a real session hit Windows' ~10,000-per-process USER-object ceiling within seconds
    // of a controller being connected, severely enough to break the tray menu and every dialog
    // with no in-app way left to recover. Root cause turned out to be unrelated to this feature
    // entirely - see Sdl2PadReader's constructor - but the failure mode was bad enough that this
    // proactive cutoff stays: if USER objects ever climb again for any other reason, this
    // degrades it to "the feature turns itself off" instead of "the whole app UI stops working."
    private DateTimeOffset _lastResourceCheckUtc = DateTimeOffset.MinValue;
    private static readonly TimeSpan ResourceCheckInterval = TimeSpan.FromMilliseconds(500);
    private const uint UserObjectSafetyThreshold = 5000;
    private bool _thresholdLogged;

    public GamepadPassthroughController(TimeProvider? clock = null)
    {
        _clock = clock ?? TimeProvider.System;
    }

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

    // The XInput slot the virtual pad currently occupies, if connected - ControllerPoller feeds
    // this to XInputPadReader so its slot-scanning never reads this app's own virtual pad back as
    // if it were a real controller.
    public int? VirtualPadUserIndex => _vigem.UserIndex;

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

        // Runs every tick regardless of active state, not just while submitting reports: a real
        // session showed the USER-object count keep climbing well past the safety threshold even
        // several seconds *after* this had already turned the feature off, which the previous
        // version of this check - nested inside "only while active" - had no visibility into at
        // all once it disabled itself. Keeping it always-on can't undo an already-leaked handle,
        // but it does mean a still-unexplained leak elsewhere always gets logged instead of
        // silently continuing unobserved the moment this feature stops being the obvious suspect.
        CheckResourceSafety();
    }

    private void CheckResourceSafety()
    {
        var now = _clock.GetUtcNow();
        if (now - _lastResourceCheckUtc < ResourceCheckInterval)
            return;
        _lastResourceCheckUtc = now;

        uint userObjects = ResourceUsageMonitor.GetUserObjectCount();
        if (userObjects < UserObjectSafetyThreshold)
        {
            _thresholdLogged = false;
            return;
        }

        // Logged once per crossing, not every 500ms for as long as it stays above the threshold -
        // a leaked USER object has no way to un-leak itself, so repeating this wouldn't add
        // information, just noise.
        if (_thresholdLogged)
            return;
        _thresholdLogged = true;

        if (!_lastAppliedActive)
        {
            AppLog.Default.Error(
                $"GamepadPassthroughController: USER object count ({userObjects}) crossed the safety threshold " +
                "while \"Use HidHide\" was already off - whatever is leaking isn't limited to this feature's own " +
                "active state.");
            return;
        }

        AppLog.Default.Error(
            $"GamepadPassthroughController: USER object count ({userObjects}) crossed the safety threshold while " +
            "active - turning \"Use HidHide\" off automatically to avoid exhausting the process's window-handle " +
            "quota.");

        AppSettings.Instance.UseHidHide = false;
        AppSettings.Instance.Save();

        if (Interlocked.CompareExchange(ref _transitioning, 1, 0) == 0)
        {
            _lastAppliedActive = false;
            _ = Task.Run(() => ApplyTransition(false, null)).ContinueWith(
                _ => Volatile.Write(ref _transitioning, 0),
                TaskScheduler.Default);
        }
    }

    private void ApplyTransition(bool active, PhysicalDeviceIdentity? device)
    {
        string label = active ? "activate" : "deactivate";
        ResourceUsageMonitor.LogSnapshot($"before-{label}");

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

        ResourceUsageMonitor.LogSnapshot($"after-{label}");
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
