namespace ControllerMagic;

// Something the user has to know or act on; raised off the UI thread.
internal enum PassthroughNotice
{
    ReconnectToFinishHiding,
    TurnedOffBySafetyCutoff,
}

internal readonly record struct PassthroughTarget(bool Hiding, bool VirtualPad, bool BlockConnection);

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
    private readonly IHidHide _hidHide;
    private readonly IVirtualPad _vigem;
    private readonly TimeProvider _clock;
    private readonly Func<bool> _settingOn;
    private readonly Func<Action, Task> _runInBackground;
    private readonly Func<IHidHide, DriverStatus> _detectDrivers;

    private volatile bool _fullscreenSuspended;
    // volatile bool, not a DriverStatus struct field: written from background tasks (the first-
    // tick probe, and Settings after a successful install) and read from the poll thread every
    // tick, so it needs to be a single primitive to be safely volatile.
    private volatile bool _driversReady;
    private bool _startupResetDone;
    // What's really in place, never what was asked for: a failed connect or a dropped virtual pad
    // has to show up here, or the real pad stays hidden with nothing replacing it.
    private volatile bool _cloaked;
    private volatile bool _virtualPadExpected;
    private readonly VirtualPadRetry _virtualPadRetry;
    // Only the poll thread starts transitions, and only once the previous one has finished.
    private Task _transition = Task.CompletedTask;
    // Once set (under the gate), nothing may cloak or connect again: a transition still running
    // when Shutdown uncloaks would otherwise leave the pad hidden after exit.
    private readonly Lock _shutdownGate = new();
    private volatile bool _shutDown;
    private static readonly TimeSpan ShutdownWait = TimeSpan.FromSeconds(2);
    private Connection? _connection;

    public event Action<PassthroughNotice>? NoticeRaised;

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

    public GamepadPassthroughController()
        : this(new HidHideBridge(), new VigemBridge(), TimeProvider.System,
            () => AppSettings.Instance.UseHidHide, action => Task.Run(action), DriverDependency.Detect)
    {
    }

    internal GamepadPassthroughController(
        IHidHide hidHide, IVirtualPad virtualPad, TimeProvider clock,
        Func<bool> settingOn, Func<Action, Task> runInBackground, Func<IHidHide, DriverStatus> detectDrivers)
    {
        _hidHide = hidHide;
        _vigem = virtualPad;
        _clock = clock;
        _settingOn = settingOn;
        _runInBackground = runInBackground;
        _detectDrivers = detectDrivers;
        _virtualPadRetry = new VirtualPadRetry(clock);
    }

    // Pure policy, isolated from driver I/O so it's directly testable: the feature only ever
    // runs with the setting on, the drivers actually present, and no fullscreen exclusion zone
    // currently suppressing it.
    internal static bool ComputeShouldBeActive(bool settingOn, bool driversReady, bool fullscreenSuspended) =>
        settingOn && driversReady && !fullscreenSuspended;

    // Hiding doesn't wait for a controller: HidHide only filters opens made after it starts, so a
    // pad has to arrive already hidden or Steam and the system grab it first.
    // A pad that can't get a virtual replacement right now stays visible rather than vanishing.
    internal static PassthroughTarget ComputeTarget(
        bool settingOn, bool driversReady, bool fullscreenSuspended,
        bool padConnected, bool connectionKnown, bool connectionBlocked, bool virtualPadUnavailable)
    {
        if (!ComputeShouldBeActive(settingOn, driversReady, fullscreenSuspended))
            return default;

        bool virtualPadWanted = padConnected && connectionKnown;
        if (virtualPadWanted && virtualPadUnavailable)
            return default;

        return new PassthroughTarget(
            Hiding: true,
            VirtualPad: virtualPadWanted,
            BlockConnection: connectionKnown && !connectionBlocked);
    }

    // Only a connection that arrived while cloaked and already block-listed is hidden from everyone.
    internal static bool ShouldAskToReconnect(bool cloakedAtArrival, bool wasAlreadyBlocked) =>
        !(cloakedAtArrival && wasAlreadyBlocked);

    // Called once at construction (fire-and-forget, off the UI thread) and again from Settings
    // right after a successful driver install - never on a timer.
    public void RefreshDriverStatus()
    {
        var status = _detectDrivers(_hidHide);
        _driversReady = status.HidHideInstalled && status.VigemInstalled;
    }

    // For Settings' toggle-enablement check: runs the same detection off the calling thread so a
    // UI-thread caller never blocks on it.
    public Task<DriverStatus> DetectDriverStatusAsync(CancellationToken ct = default) =>
        Task.Run(() => _detectDrivers(_hidHide), ct);

    public void SetFullscreenSuspended(bool suspended) => _fullscreenSuspended = suspended;

    // The XInput slots the virtual pad may occupy, a bit each - ControllerPoller feeds this to
    // XInputPadReader so its slot-scanning never reads this app's own virtual pad back as if it
    // were a real controller.
    public int VirtualPadXInputSlots => _vigem.ExcludedXInputSlots;

    public void Tick(PadState pad, bool gotPad, PhysicalDeviceIdentity? deviceIdentity, int connectionSerial)
    {
        if (_shutDown)
            return;

        if (!_startupResetDone)
        {
            _startupResetDone = true;
            // Guaranteed crash/force-kill recovery: unconditionally clear cloaking left over from
            // a previous run that didn't exit cleanly, before evaluating current settings at all.
            _ = _runInBackground(() =>
            {
                _hidHide.SetCloakingEnabled(false);
                RefreshDriverStatus();
            });
        }

        TrackConnection(deviceIdentity, connectionSerial);

        if (_transition.IsCompleted)
            StartTransitionIfNeeded(gotPad);

        if (gotPad && _vigem.IsConnected)
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

    private void StartTransitionIfNeeded(bool gotPad)
    {
        NoteVirtualPadDropped();

        bool settingOn = _settingOn();
        bool driversReady = _driversReady;
        bool fullscreenSuspended = _fullscreenSuspended;
        if (!ComputeShouldBeActive(settingOn, driversReady, fullscreenSuspended))
            _virtualPadRetry.Reset();

        var target = ComputeTarget(
            settingOn, driversReady, fullscreenSuspended,
            gotPad, _connection != null, _connection?.BlockAttempted ?? false,
            virtualPadUnavailable: !_virtualPadRetry.CanAttempt);
        var current = new PassthroughTarget(_cloaked, _vigem.IsConnected, BlockConnection: false);
        if (target == current)
            return;

        var toBlock = target.BlockConnection ? _connection : null;
        _transition = _runInBackground(() => ApplyTransition(current, target, toBlock));
    }

    // A failed submit disconnects the bridge on its own; counting that as a failed attempt paces a
    // pad that keeps failing instead of reconnecting it every tick.
    private void NoteVirtualPadDropped()
    {
        if (!_virtualPadExpected || _vigem.IsConnected)
            return;

        _virtualPadExpected = false;
        RecordConnectFailure("dropped");
    }

    private void TrackConnection(PhysicalDeviceIdentity? deviceIdentity, int connectionSerial)
    {
        if (deviceIdentity is not { IsValid: true } device || connectionSerial == _connection?.Serial)
            return;

        _connection = new Connection(connectionSerial, device, cloakedAtArrival: _cloaked);
        _virtualPadRetry.Reset();
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

        if (!_cloaked)
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

        // The next Tick sees the setting off and stands everything down through the normal path.
        AppSettings.Instance.UseHidHide = false;
        AppSettings.Instance.Save();
        NoticeRaised?.Invoke(PassthroughNotice.TurnedOffBySafetyCutoff);
    }

    // Blocks before cloaking so a newly hidden device never has an unfiltered moment, and connects
    // the virtual pad before cloaking so a failed connect never leaves the real one hidden. Turning
    // off only flips the cloak flag - allow-list and block-list entries stay so re-activating is instant.
    private void ApplyTransition(PassthroughTarget current, PassthroughTarget target, Connection? toBlock)
    {
        if (_shutDown)
            return;

        try
        {
            ResourceUsageMonitor.LogSnapshot($"before-transition {current} -> {target}");

            if (target.Hiding && !current.Hiding)
                _hidHide.EnsureAppAllowListed();

            if (toBlock != null)
                BlockConnection(toBlock);

            bool virtualPadReady = !target.VirtualPad || current.VirtualPad || ConnectVirtualPad();
            SetCloaked(target.Hiding && virtualPadReady);

            if (!target.VirtualPad && current.VirtualPad)
                DisconnectVirtualPad();

            ResourceUsageMonitor.LogSnapshot("after-transition");
        }
        catch (Exception ex)
        {
            AppLog.Default.Warning($"GamepadPassthroughController: transition {current} -> {target} failed", ex);
        }
    }

    private bool ConnectVirtualPad()
    {
        if (_shutDown)
            return false;

        if (!_vigem.TryConnect())
        {
            RecordConnectFailure("failed to connect");
            return false;
        }

        // Shutdown may have disconnected while this was still connecting.
        if (_shutDown)
        {
            _vigem.Disconnect();
            return false;
        }

        _virtualPadRetry.RecordConnected();
        _virtualPadExpected = true;
        return true;
    }

    private void RecordConnectFailure(string what)
    {
        int failures = _virtualPadRetry.RecordFailure();
        string next = failures < VirtualPadRetry.MaxAttempts
            ? "retrying shortly"
            : "giving up until the controller reconnects";
        AppLog.Default.Warning(
            $"GamepadPassthroughController: virtual pad {what} ({failures} of {VirtualPadRetry.MaxAttempts}); " +
            $"real controller left visible, {next}");
    }

    private void DisconnectVirtualPad()
    {
        _virtualPadExpected = false;
        _vigem.Disconnect();
    }

    private void SetCloaked(bool cloaked)
    {
        lock (_shutdownGate)
        {
            if (cloaked == _cloaked || (cloaked && _shutDown))
                return;

            _hidHide.SetCloakingEnabled(cloaked);
            _cloaked = cloaked;
        }
    }

    private void BlockConnection(Connection connection)
    {
        var device = connection.Device;
        var result = _hidHide.BlockDevice(device.InterfacePath);
        connection.BlockAttempted = true;
        AppLog.Default.Info(
            $"GamepadPassthroughController: block {device.VendorId:X4}:{device.ProductId:X4} -> {result} " +
            $"(cloaked at arrival: {connection.CloakedAtArrival})");

        if (result != BlockResult.Failed && ShouldAskToReconnect(connection.CloakedAtArrival, result == BlockResult.AlreadyBlocked))
            NoticeRaised?.Invoke(PassthroughNotice.ReconnectToFinishHiding);
    }

    // One SDL open of a physical pad; blocking is attempted once per connection.
    private sealed class Connection(int serial, PhysicalDeviceIdentity device, bool cloakedAtArrival)
    {
        private volatile bool _blockAttempted;

        public int Serial { get; } = serial;
        public PhysicalDeviceIdentity Device { get; } = device;
        public bool CloakedAtArrival { get; } = cloakedAtArrival;

        public bool BlockAttempted
        {
            get => _blockAttempted;
            set => _blockAttempted = value;
        }
    }

    // Best-effort, called from ControllerPoller.Loop's finally block right before the poll
    // thread exits - covers both the ordinary clean-exit path (Stop() joins this thread) and a
    // same-process crash on the poll thread. A short synchronous wait here is a deliberate,
    // narrow exception to "never block on async": this is a genuine shutdown boundary, and the
    // guaranteed startup reset above is what actually keeps a missed shutdown from mattering.
    // Final: after this, Tick does nothing.
    public void Shutdown()
    {
        lock (_shutdownGate)
        {
            if (_shutDown)
                return;
            _shutDown = true;
        }

        try
        {
            if (!_transition.Wait(ShutdownWait))
                AppLog.Default.Warning("GamepadPassthroughController: a transition was still running at shutdown");

            DisconnectVirtualPad();
            lock (_shutdownGate)
            {
                _hidHide.SetCloakingEnabled(false);
                _cloaked = false;
            }
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
