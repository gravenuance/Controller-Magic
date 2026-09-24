using System.Runtime.InteropServices;
using SDL2;

namespace ControllerMagic;

// Reads non-Xbox controllers (PS4/PS5, Switch Pro Controller, third-party pads, wired or
// wireless) via SDL2's GameController API. Unlike hand-rolled HID usage-page parsing, SDL ships
// a crowd-sourced, per-device-verified mapping database (gamecontrollerdb.txt) covering
// thousands of real controllers, so SDL_CONTROLLER_BUTTON_A etc. are already correct for the
// device's actual layout — no guessing axis order or hardcoding vendor-specific button swaps.
//
// SDL_JOYSTICK_ALLOW_BACKGROUND_EVENTS is required for a background tray app like this one:
// without it, SDL (like most input APIs) only updates controller state for the focused window.
//
// Not thread-safe by design: construct, use, and dispose this from a single dedicated thread
// (SDL's event queue is meant to be pumped consistently from one thread).
internal sealed class Sdl2PadReader : IDisposable
{
    private readonly bool _initialized;
    private IntPtr _controller;
    private int _controllerInstanceId = -1;

    // The pinned ppy.SDL2-CS binding doesn't wrap SDL_JoystickPath (added to SDL2 after this
    // binding's SDL_Joystick* surface was generated), even though the bundled native SDL2.dll
    // exports it - declared directly here rather than hand-rolling raw HID/SetupAPI enumeration,
    // which this codebase has no precedent for at all.
    [DllImport("SDL2.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr SDL_JoystickPath(IntPtr joystick);

    [DllImport("SDL2.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr SDL_JoystickPathForIndex(int deviceIndex);

    // SDL's XInput backend names its devices "XInput#<slot>" (SDL_xinputjoystick.c).
    private const string XInputPathPrefix = "XInput#";

    // Identifies the currently-open physical controller for HidHide, computed once when opened
    // (not per-tick) since it never changes for the lifetime of one connection. Null whenever no
    // controller is open, or SDL couldn't report a path for this specific device.
    public PhysicalDeviceIdentity? CurrentDeviceIdentity { get; private set; }

    // Bumped on every successful open, so callers can tell a reconnect apart from the same session.
    public int ConnectionSerial { get; private set; }

    private Color? _appliedLightbar;

    // DS5EffectsState_t from SDL's hidapi PS5 driver: only the player-LED fields are filled in.
    private const int DualSenseEffectSize = 47;
    private const int EnableBits2Offset = 1;
    private const byte EnablePlayerLights = 0x10;
    private const int PlayerLightsOffset = 43;
    private const byte PlayerLightsInstant = 0x20;
    private static readonly TimeSpan PlayerLightsRefresh = TimeSpan.FromSeconds(3);

    private readonly TimeProvider _clock;
    private bool _isDualSense;
    private byte? _appliedPlayerLights;
    private long _playerLightsSentAt;
    private bool _playerLightsFailureLogged;

    [DllImport("SDL2.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern unsafe int SDL_GameControllerGetSensorDataWithTimestamp(
        IntPtr gamecontroller, SDL.SDL_SensorType type, out ulong timestamp, float* data, int numValues);

    // Long enough to ride out ordinary Bluetooth packet loss without dropping a held drag.
    private static readonly TimeSpan ReportsStaleAfter = TimeSpan.FromMilliseconds(200);

    private readonly ReportWatchdog _watchdog;
    private bool _hasReportStamp;
    private bool _reportsStopped;
    private int _dropoutCount;

    public Sdl2PadReader(TimeProvider clock)
    {
        _clock = clock;
        _watchdog = new ReportWatchdog(clock, ReportsStaleAfter);

        SDL.SDL_SetHint(SDL.SDL_HINT_JOYSTICK_ALLOW_BACKGROUND_EVENTS, "1");

        // Root cause of a severe Windows USER-object leak (climbing to the ~10,000-per-process
        // ceiling within seconds, breaking the tray menu and every dialog), confirmed independent
        // of this app's own HidHide/ViGEm feature - it happens purely from SDL_Init(GAMECONTROLLER)
        // running every launch with a controller connected. Traced to ppy's SDL2 fork's own
        // source: the RAWINPUT joystick driver (the default active backend here for an Xbox-type
        // pad) unconditionally calls RAWINPUT_InitWindowsGamingInput() to correlate with
        // Windows.Gaming.Input for extended controller features - that WinRT activation is what
        // leaks. SDL_JOYSTICK_WGI (a compile-time #ifdef, not a runtime hint - a dead end tried
        // first) doesn't gate this at all; SDL_HINT_JOYSTICK_RAWINPUT does, by disabling the whole
        // driver. Falls back to SDL's XInput/DirectInput backends, which this app already relies
        // on primarily anyway via its own separate XInputPadReader.
        SDL.SDL_SetHint(SDL.SDL_HINT_JOYSTICK_RAWINPUT, "0");

        // Over Bluetooth a DualSense starts in simple reports, which carry no touchpad data and
        // ignore lightbar changes; this switches it to enhanced reports until it reconnects.
        SDL.SDL_SetHint(SDL.SDL_HINT_JOYSTICK_HIDAPI_PS5_RUMBLE, "1");

        // The player LEDs show battery instead of SDL's player-number pattern.
        SDL.SDL_SetHint(SDL.SDL_HINT_JOYSTICK_HIDAPI_PS5_PLAYER_LED, "0");

        _initialized = SDL.SDL_Init(SDL.SDL_INIT_GAMECONTROLLER) == 0;

        // The accelerometer runs only as a per-report liveness stamp; its events would just flood the queue.
        if (_initialized)
            _ = SDL.SDL_EventState(SDL.SDL_EventType.SDL_CONTROLLERSENSORUPDATE, SDL.SDL_IGNORE);
    }

    public bool TryGetLatest(out PadState state)
    {
        state = default;
        if (!_initialized)
            return false;

        if (_controller == IntPtr.Zero || SDL.SDL_GameControllerGetAttached(_controller) == SDL.SDL_bool.SDL_FALSE)
        {
            CloseController();
            return false;
        }

        state = TrackReportsStopped(_watchdog.IsStale(ReadReportStamp()))
            ? new PadState { IsConnected = true }
            : Read(_controller);
        return true;
    }

    private unsafe ulong? ReadReportStamp()
    {
        if (!_hasReportStamp)
            return null;

        float* data = stackalloc float[3];
        return SDL_GameControllerGetSensorDataWithTimestamp(_controller, SDL.SDL_SensorType.SDL_SENSOR_ACCEL, out ulong stamp, data, 3) == 0
            ? stamp
            : null;
    }

    // A pad at the edge of range can drop out many times a minute, so only the first is logged in full.
    private bool TrackReportsStopped(bool stopped)
    {
        if (stopped == _reportsStopped)
            return stopped;

        _reportsStopped = stopped;
        if (stopped && _dropoutCount++ == 0)
            AppLog.Default.Warning($"Sdl2PadReader: no reports for {ReportsStaleAfter.TotalMilliseconds:0}ms (out of range?), holding the pad neutral");
        return stopped;
    }

    // Public so the poll loop can run this every tick regardless of which source ends up
    // supplying the frame - see the call site in ControllerPoller.Loop. Draining the event queue
    // alone isn't enough: TryOpenFirstAvailable() also needs to run whenever no controller is
    // currently open, or a second, non-XInput controller plugged in while an XInput one is already
    // connected and successfully reading would never get picked up - since TryGetLatest (the only
    // other place that used to call it) is skipped by `gotXInput ||` short-circuiting past it for
    // as long as XInput keeps winning. Restarting the app "fixed" it only because that reset
    // _controller back to IntPtr.Zero and gave this a fresh chance to run.
    public void PumpEvents()
    {
        while (SDL.SDL_PollEvent(out var e) != 0)
        {
            if (e.type == SDL.SDL_EventType.SDL_CONTROLLERDEVICEREMOVED && e.cdevice.which == _controllerInstanceId)
                CloseController();
        }

        if (_controller == IntPtr.Zero)
            TryOpenFirstAvailable();
    }

    // Sent only when the colour changes; a pad without a lightbar is simply skipped.
    public void SetLightbar(Color color)
    {
        if (_controller == IntPtr.Zero || _appliedLightbar == color)
            return;

        _appliedLightbar = color;
        if (SDL.SDL_GameControllerHasLED(_controller) != SDL.SDL_bool.SDL_TRUE)
            return;

        if (SDL.SDL_GameControllerSetLED(_controller, color.R, color.G, color.B) != 0)
            AppLog.Default.Warning($"Sdl2PadReader: failed to set the lightbar: {SDL.SDL_GetError()}");
    }

    public BatteryLevel Battery => _controller == IntPtr.Zero
        ? BatteryLevel.Unknown
        : SDL.SDL_JoystickCurrentPowerLevel(SDL.SDL_GameControllerGetJoystick(_controller)) switch
        {
            SDL.SDL_JoystickPowerLevel.SDL_JOYSTICK_POWER_EMPTY => BatteryLevel.Empty,
            SDL.SDL_JoystickPowerLevel.SDL_JOYSTICK_POWER_LOW => BatteryLevel.Low,
            SDL.SDL_JoystickPowerLevel.SDL_JOYSTICK_POWER_MEDIUM => BatteryLevel.Medium,
            SDL.SDL_JoystickPowerLevel.SDL_JOYSTICK_POWER_FULL => BatteryLevel.Full,
            SDL.SDL_JoystickPowerLevel.SDL_JOYSTICK_POWER_WIRED => BatteryLevel.Wired,
            _ => BatteryLevel.Unknown,
        };

    // SDL resets and rewrites the player LEDs once a Bluetooth connection settles, wiping an early
    // pattern, so this re-sends on a slow cadence as well as on change.
    public unsafe void SetPlayerLights(byte mask)
    {
        if (!_isDualSense)
            return;
        if (_appliedPlayerLights == mask && _clock.GetElapsedTime(_playerLightsSentAt) < PlayerLightsRefresh)
            return;

        bool changed = _appliedPlayerLights != mask;
        _appliedPlayerLights = mask;
        _playerLightsSentAt = _clock.GetTimestamp();

        byte* effect = stackalloc byte[DualSenseEffectSize];
        new Span<byte>(effect, DualSenseEffectSize).Clear();
        effect[EnableBits2Offset] = EnablePlayerLights;
        effect[PlayerLightsOffset] = (byte)(mask | PlayerLightsInstant);

        int result = SDL.SDL_GameControllerSendEffect(_controller, (IntPtr)effect, DualSenseEffectSize);
        if (changed)
            AppLog.Default.Info($"Sdl2PadReader: player LEDs 0x{mask:X2} for battery {Battery} (SendEffect -> {result})");

        if (result != 0 && !_playerLightsFailureLogged)
        {
            _playerLightsFailureLogged = true;
            AppLog.Default.Warning($"Sdl2PadReader: failed to set the player LEDs: {SDL.SDL_GetError()}");
        }
    }

    private void TryOpenFirstAvailable()
    {
        int count = SDL.SDL_NumJoysticks();
        for (int i = 0; i < count; i++)
        {
            if (SDL.SDL_IsGameController(i) != SDL.SDL_bool.SDL_TRUE || IsXInputBacked(PathForIndex(i)))
                continue;

            var handle = SDL.SDL_GameControllerOpen(i);
            if (handle == IntPtr.Zero)
                continue;

            _controller = handle;
            IntPtr joystick = SDL.SDL_GameControllerGetJoystick(handle);
            _controllerInstanceId = SDL.SDL_JoystickInstanceID(joystick);
            CurrentDeviceIdentity = TryGetDeviceIdentity(handle, joystick);
            var type = SDL.SDL_GameControllerGetType(handle);
            _isDualSense = type == SDL.SDL_GameControllerType.SDL_CONTROLLER_TYPE_PS5;
            _hasReportStamp = TryEnableReportStamp(handle);
            AppLog.Default.Info($"Sdl2PadReader: opened {type} ({SDL.SDL_GameControllerName(handle)}), range guard {(_hasReportStamp ? "on" : "unavailable")}");
            ConnectionSerial++;
            return;
        }
    }

    private static bool TryEnableReportStamp(IntPtr controller) =>
        SDL.SDL_GameControllerHasSensor(controller, SDL.SDL_SensorType.SDL_SENSOR_ACCEL) == SDL.SDL_bool.SDL_TRUE
        && SDL.SDL_GameControllerSetSensorEnabled(controller, SDL.SDL_SensorType.SDL_SENSOR_ACCEL, SDL.SDL_bool.SDL_TRUE) == 0;

    // XInputPadReader owns XInput pads. Opening one here also caught this app's own virtual pad, which
    // then fed itself and kept the slot, so a returning Bluetooth DualSense was never opened.
    internal static bool IsXInputBacked(string? path) =>
        path != null && path.StartsWith(XInputPathPrefix, StringComparison.Ordinal);

    private static string? PathForIndex(int deviceIndex)
    {
        IntPtr pathPtr = SDL_JoystickPathForIndex(deviceIndex);
        return pathPtr == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(pathPtr);
    }

    // SDL_JoystickPath only resolves for a device SDL has actually opened as a game controller
    // (i.e. one present in gamecontrollerdb.txt) - an unmapped device leaves this null, which
    // callers must treat as "can't be hidden" rather than guessing at an identity.
    private static PhysicalDeviceIdentity? TryGetDeviceIdentity(IntPtr controller, IntPtr joystick)
    {
        IntPtr pathPtr = SDL_JoystickPath(joystick);
        string? path = pathPtr == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(pathPtr);
        if (string.IsNullOrEmpty(path))
            return null;

        ushort vendor = SDL.SDL_GameControllerGetVendor(controller);
        ushort product = SDL.SDL_GameControllerGetProduct(controller);
        return new PhysicalDeviceIdentity(path, vendor, product);
    }

    private void CloseController()
    {
        if (_controller != IntPtr.Zero)
            SDL.SDL_GameControllerClose(_controller);

        _controller = IntPtr.Zero;
        _controllerInstanceId = -1;
        CurrentDeviceIdentity = null;
        _appliedLightbar = null;
        _isDualSense = false;
        _appliedPlayerLights = null;
        _playerLightsFailureLogged = false;
        if (_dropoutCount > 0)
            AppLog.Default.Info($"Sdl2PadReader: {_dropoutCount} report dropout(s) during this connection");

        _hasReportStamp = false;
        _watchdog.Reset();
        _reportsStopped = false;
        _dropoutCount = 0;
    }

    private static PadState Read(IntPtr controller)
    {
        PadButtons buttons = PadButtons.None;
        if (IsDown(controller, SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_A)) buttons |= PadButtons.A;
        if (IsDown(controller, SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_B)) buttons |= PadButtons.B;
        if (IsDown(controller, SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_X)) buttons |= PadButtons.X;
        if (IsDown(controller, SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_Y)) buttons |= PadButtons.Y;
        if (IsDown(controller, SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_LEFTSHOULDER)) buttons |= PadButtons.LeftShoulder;
        if (IsDown(controller, SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_RIGHTSHOULDER)) buttons |= PadButtons.RightShoulder;
        if (IsDown(controller, SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_BACK)) buttons |= PadButtons.Back;
        if (IsDown(controller, SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_START)) buttons |= PadButtons.Start;
        if (IsDown(controller, SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_LEFTSTICK)) buttons |= PadButtons.LeftThumb;
        if (IsDown(controller, SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_RIGHTSTICK)) buttons |= PadButtons.RightThumb;
        if (IsDown(controller, SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_DPAD_UP)) buttons |= PadButtons.DPadUp;
        if (IsDown(controller, SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_DPAD_DOWN)) buttons |= PadButtons.DPadDown;
        if (IsDown(controller, SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_DPAD_LEFT)) buttons |= PadButtons.DPadLeft;
        if (IsDown(controller, SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_DPAD_RIGHT)) buttons |= PadButtons.DPadRight;
        if (IsDown(controller, SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_TOUCHPAD)) buttons |= PadButtons.TouchpadClick;

        // SDL's Y axes increase downward (like raw HID); invert so positive means "up",
        // matching the convention the rest of this app expects.
        short leftX = SDL.SDL_GameControllerGetAxis(controller, SDL.SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_LEFTX);
        short leftY = InvertAxis(SDL.SDL_GameControllerGetAxis(controller, SDL.SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_LEFTY));
        short rightX = SDL.SDL_GameControllerGetAxis(controller, SDL.SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_RIGHTX);
        short rightY = InvertAxis(SDL.SDL_GameControllerGetAxis(controller, SDL.SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_RIGHTY));

        byte leftTrigger = TriggerToByte(SDL.SDL_GameControllerGetAxis(controller, SDL.SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_TRIGGERLEFT));
        byte rightTrigger = TriggerToByte(SDL.SDL_GameControllerGetAxis(controller, SDL.SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_TRIGGERRIGHT));

        bool touchActive = TryReadFirstFinger(controller, out float touchX, out float touchY);

        return new PadState
        {
            IsConnected = true,
            LeftThumbX = leftX,
            LeftThumbY = leftY,
            RightThumbX = rightX,
            RightThumbY = rightY,
            LeftTrigger = leftTrigger,
            RightTrigger = rightTrigger,
            Buttons = buttons,
            TouchActive = touchActive,
            TouchX = touchX,
            TouchY = touchY
        };
    }

    private static bool TryReadFirstFinger(IntPtr controller, out float x, out float y)
    {
        x = 0;
        y = 0;
        if (SDL.SDL_GameControllerGetNumTouchpads(controller) == 0)
            return false;

        return SDL.SDL_GameControllerGetTouchpadFinger(controller, 0, 0, out byte state, out x, out y, out _) == 0
            && state != 0;
    }

    private static bool IsDown(IntPtr controller, SDL.SDL_GameControllerButton button) =>
        SDL.SDL_GameControllerGetButton(controller, button) != 0;

    private static short InvertAxis(short value) =>
        value == short.MinValue ? short.MaxValue : (short)-value;

    private static byte TriggerToByte(short value)
    {
        if (value < 0) value = 0;
        return (byte)(value >> 7); // SDL trigger range 0..32767 -> 0..255
    }

    public void Dispose()
    {
        CloseController();
        if (_initialized)
            SDL.SDL_Quit();
    }
}
