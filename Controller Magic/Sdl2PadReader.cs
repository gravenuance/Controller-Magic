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

    public Sdl2PadReader()
    {
        SDL.SDL_SetHint(SDL.SDL_HINT_JOYSTICK_ALLOW_BACKGROUND_EVENTS, "1");
        _initialized = SDL.SDL_Init(SDL.SDL_INIT_GAMECONTROLLER) == 0;
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

        state = Read(_controller);
        return true;
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

    private void TryOpenFirstAvailable()
    {
        int count = SDL.SDL_NumJoysticks();
        for (int i = 0; i < count; i++)
        {
            if (SDL.SDL_IsGameController(i) != SDL.SDL_bool.SDL_TRUE)
                continue;

            var handle = SDL.SDL_GameControllerOpen(i);
            if (handle == IntPtr.Zero)
                continue;

            _controller = handle;
            _controllerInstanceId = SDL.SDL_JoystickInstanceID(SDL.SDL_GameControllerGetJoystick(handle));
            return;
        }
    }

    private void CloseController()
    {
        if (_controller != IntPtr.Zero)
            SDL.SDL_GameControllerClose(_controller);

        _controller = IntPtr.Zero;
        _controllerInstanceId = -1;
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

        // SDL's Y axes increase downward (like raw HID); invert so positive means "up",
        // matching the convention the rest of this app expects.
        short leftX = SDL.SDL_GameControllerGetAxis(controller, SDL.SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_LEFTX);
        short leftY = InvertAxis(SDL.SDL_GameControllerGetAxis(controller, SDL.SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_LEFTY));
        short rightX = SDL.SDL_GameControllerGetAxis(controller, SDL.SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_RIGHTX);
        short rightY = InvertAxis(SDL.SDL_GameControllerGetAxis(controller, SDL.SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_RIGHTY));

        byte leftTrigger = TriggerToByte(SDL.SDL_GameControllerGetAxis(controller, SDL.SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_TRIGGERLEFT));
        byte rightTrigger = TriggerToByte(SDL.SDL_GameControllerGetAxis(controller, SDL.SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_TRIGGERRIGHT));

        return new PadState
        {
            IsConnected = true,
            LeftThumbX = leftX,
            LeftThumbY = leftY,
            RightThumbX = rightX,
            RightThumbY = rightY,
            LeftTrigger = leftTrigger,
            RightTrigger = rightTrigger,
            Buttons = buttons
        };
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
