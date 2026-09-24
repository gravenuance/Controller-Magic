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
    private PassthroughTarget _applied;
    // Only the poll thread starts transitions, and only once the previous one has finished.
    private Task _transition = Task.CompletedTask;
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
    }

    // Pure policy, isolated from driver I/O so it's directly testable: the feature only ever
    // runs with the setting on, the drivers actually present, and no fullscreen exclusion zone
    // currently suppressing it.
    internal static bool ComputeShouldBeActive(bool settingOn, bool driversReady, bool fullscreenSuspended) =>
        settingOn && driversReady && !fullscreenSuspended;

    // Hiding doesn't wait for a controller: HidHide only filters opens made after it starts, so a
    // pad has to arrive already hidden or Steam and the system grab it first.
    internal static PassthroughTarget ComputeTarget(
        bool settingOn, bool driversReady, bool fullscreenSuspended,
        bool padConnected, bool connectionKnown, bool connectionBlocked)
    {
        if (!ComputeShouldBeActive(settingOn, driversReady, fullscreenSuspended))
            return default;

        return new PassthroughTarget(
            Hiding: true,
            VirtualPad: padConnected && connectionKnown,
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

    // The XInput slot the virtual pad currently occupies, if connected - ControllerPoller feeds
    // this to XInputPadReader so its slot-scanning never reads this app's own virtual pad back as
    // if it were a real controller.
    public int? VirtualPadUserIndex => _vigem.UserIndex;

    public void Tick(PadState pad, bool gotPad, PhysicalDeviceIdentity? deviceIdentity, int connectionSerial)
    {
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

        var target = ComputeTarget(
            _settingOn(), _driversReady, _fullscreenSuspended,
            gotPad, _connection != null, _connection?.BlockAttempted ?? false);

        if (target != _applied && _transition.IsCompleted)
        {
            var previous = _applied;
            var toBlock = target.BlockConnection ? _connection : null;
            _applied = target with { BlockConnection = false };
            _transition = _runInBackground(() => ApplyTransition(previous, target, toBlock));
        }

        if (_applied.VirtualPad && gotPad)
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

    private void TrackConnection(PhysicalDeviceIdentity? deviceIdentity, int connectionSerial)
    {
        if (deviceIdentity is not { IsValid: true } device || connectionSerial == _connection?.Serial)
            return;

        _connection = new Connection(connectionSerial, device, cloakedAtArrival: _applied.Hiding);
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

        if (!_applied.Hiding)
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

    // Blocks before cloaking so a newly hidden device never has an unfiltered moment. Turning off
    // only flips the cloak flag - allow-list and block-list entries stay so re-activating is instant.
    private void ApplyTransition(PassthroughTarget previous, PassthroughTarget target, Connection? toBlock)
    {
        ResourceUsageMonitor.LogSnapshot($"before-transition {previous} -> {target}");

        if (target.Hiding && !previous.Hiding)
            _hidHide.EnsureAppAllowListed();

        if (toBlock != null)
            BlockConnection(toBlock);

        if (target.Hiding != previous.Hiding)
            _hidHide.SetCloakingEnabled(target.Hiding);

        if (target.VirtualPad && !previous.VirtualPad)
            _vigem.TryConnect();
        else if (!target.VirtualPad && previous.VirtualPad)
            _vigem.Disconnect();

        ResourceUsageMonitor.LogSnapshot("after-transition");
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
