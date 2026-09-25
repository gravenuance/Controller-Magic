using System.Runtime.InteropServices;

namespace ControllerMagic
{
    internal readonly record struct WheelSelection(int Sector, int Ring);

    internal sealed class ControllerPoller : IDisposable
    {
        internal struct KeyEntry
        {
            public ushort Vk;
            public char Display;
            public bool HasMod;

            public KeyEntry(ushort vk, char display, bool hasMod = false)
            {
                Vk = vk;
                Display = display;
                HasMod = hasMod;
            }
        }

        // VK aliases
        private const ushort VK_0 = 0x30;
        private const ushort VK_1 = 0x31;
        private const ushort VK_2 = 0x32;
        private const ushort VK_3 = 0x33;
        private const ushort VK_4 = 0x34;
        private const ushort VK_5 = 0x35;
        private const ushort VK_6 = 0x36;
        private const ushort VK_7 = 0x37;
        private const ushort VK_8 = 0x38;
        private const ushort VK_9 = 0x39;

        // letters
        private const ushort VK_A = 0x41;
        private const ushort VK_B = 0x42;
        private const ushort VK_C = 0x43;
        private const ushort VK_D = 0x44;
        private const ushort VK_E = 0x45;
        private const ushort VK_F = 0x46;
        private const ushort VK_G = 0x47;
        private const ushort VK_H = 0x48;
        private const ushort VK_I = 0x49;
        private const ushort VK_J = 0x4A;
        private const ushort VK_K = 0x4B;
        private const ushort VK_L = 0x4C;
        private const ushort VK_M = 0x4D;
        private const ushort VK_N = 0x4E;
        private const ushort VK_O = 0x4F;
        private const ushort VK_P = 0x50;
        private const ushort VK_Q = 0x51;
        private const ushort VK_R = 0x52;
        private const ushort VK_S = 0x53;
        private const ushort VK_T = 0x54;
        private const ushort VK_U = 0x55;
        private const ushort VK_V = 0x56;
        private const ushort VK_W = 0x57;
        private const ushort VK_X = 0x58;
        private const ushort VK_Y = 0x59;
        private const ushort VK_Z = 0x5A;

        // punctuation (US layout)
        private const ushort VK_OEM_MINUS = 0xBD; // -
        private const ushort VK_OEM_PLUS = 0xBB; // =
        private const ushort VK_OEM_COMMA = 0xBC; // ,
        private const ushort VK_OEM_PERIOD = 0xBE; // .
        private const ushort VK_OEM_1 = 0xBA; // ; :
        private const ushort VK_OEM_2 = 0xBF; // / ?
        private const ushort VK_OEM_3 = 0xC0; // ` ~
        private const ushort VK_OEM_4 = 0xDB; // [
        private const ushort VK_OEM_5 = 0xDC; // \
        private const ushort VK_OEM_6 = 0xDD; // ]
        private const ushort VK_OEM_7 = 0xDE; // ' "

        private static readonly KeyEntry[,,] Daisywheel =
        {
            {
                {
                    new KeyEntry(VK_E, 'e'),
                    new KeyEntry(VK_H, 'h'),
                    new KeyEntry(VK_G, 'g'),
                    new KeyEntry(VK_Q,    'q'),
                },

                {
                    new KeyEntry(VK_T, 't'),
                    new KeyEntry(VK_D, 'd'),
                    new KeyEntry(VK_Y, 'y'),
                    new KeyEntry(VK_Z, 'z'),
                },

                {
                    new KeyEntry(VK_A, 'a'),
                    new KeyEntry(VK_L, 'l'),
                    new KeyEntry(VK_W, 'w'),
                    new KeyEntry(0,    '\0'),
                },
                {
                    new KeyEntry(VK_O, 'o'),
                    new KeyEntry(VK_C, 'c'),
                    new KeyEntry(VK_B, 'b'),
                    new KeyEntry(0,    '\0'),
                },
                {
                    new KeyEntry(VK_I, 'i'),
                    new KeyEntry(VK_U, 'u'),
                    new KeyEntry(VK_V, 'v'),
                    new KeyEntry(0,    '\0'),
                },
                {
                    new KeyEntry(VK_N, 'n'),
                    new KeyEntry(VK_M, 'm'),
                    new KeyEntry(VK_K, 'k'),
                    new KeyEntry(0,    '\0'),
                },
                {
                    new KeyEntry(VK_S, 's'),
                    new KeyEntry(VK_P, 'p'),
                    new KeyEntry(VK_J, 'j'),
                    new KeyEntry(0,    '\0'),
                },
                {
                    new KeyEntry(VK_R, 'r'),
                    new KeyEntry(VK_F, 'f'),
                    new KeyEntry(VK_X, 'x'),
                    new KeyEntry(0, '\0'),
                },
            },
            {
                {
                    new KeyEntry(VK_1, '1'),
                    new KeyEntry(VK_9, '9'),
                    new KeyEntry(0, '\0'),
                    new KeyEntry(0,    '\0'),
                },
                {
                    new KeyEntry(VK_2, '2'),
                    new KeyEntry(VK_0, '0'),
                    new KeyEntry(0, '\0'),
                    new KeyEntry(0,    '\0'),
                },
                {
                    new KeyEntry(VK_3, '3'),
                    new KeyEntry(0, '\0'),
                    new KeyEntry(0, '\0'),
                    new KeyEntry(0,    '\0'),
                },
                {
                    new KeyEntry(VK_4, '4'),
                    new KeyEntry(0,    '\0'),
                    new KeyEntry(0,    '\0'),
                    new KeyEntry(0,    '\0'),
                },
                {
                    new KeyEntry(VK_5, '5'),
                    new KeyEntry(0, '\0'),
                    new KeyEntry(0, '\0'),
                    new KeyEntry(0, '\0'),
                },
                {
                    new KeyEntry(VK_6, '6'),
                    new KeyEntry(0, '\0'),
                    new KeyEntry(0, '\0'),
                    new KeyEntry(0, '\0'),
                },
                {
                    new KeyEntry(VK_7, '7'),
                    new KeyEntry(0, '\0'),
                    new KeyEntry(0, '\0'),
                    new KeyEntry(0, '\0'),
                },
                {
                    new KeyEntry(VK_8, '8'),
                    new KeyEntry(0, '\0'),
                    new KeyEntry(0, '\0'),
                    new KeyEntry(0, '\0'),
                },
            },
            {
                {
                    new KeyEntry(VK_OEM_PERIOD, '.'),
                    new KeyEntry(VK_OEM_COMMA,  ','),
                    new KeyEntry(VK_OEM_2,      '?', true), // Shift+'/' for ? on US
                    new KeyEntry(0,             '\0'),
                },
                {
                    new KeyEntry(VK_1, '!', true),   // Shift+1
                    new KeyEntry(VK_2, '@', true),   // Shift+2
                    new KeyEntry(VK_3, '#', true),   // Shift+3
                    new KeyEntry(0,    '\0'),
                },
                {
                    new KeyEntry(VK_OEM_MINUS, '-'),
                    new KeyEntry(VK_OEM_MINUS, '_', true), // with Shift
                    new KeyEntry(VK_OEM_PLUS,  '+', true), // Shift+'='
                    new KeyEntry(0,            '\0'),
                },
                {
                    new KeyEntry(VK_OEM_PLUS, '='),
                    new KeyEntry(VK_7,        '&', true), // Shift+7
                    new KeyEntry(VK_8,        '*', true), // Shift+8
                    new KeyEntry(0,           '\0'),
                },
                {
                    new KeyEntry(VK_OEM_7, '\''),
                    new KeyEntry(VK_OEM_7, '"', true),
                    new KeyEntry(0,   '\0'),
                    new KeyEntry(0,   '\0'),
                },
                {
                    new KeyEntry(VK_OEM_1, ';'),
                    new KeyEntry(VK_OEM_1, ':', true),
                    new KeyEntry(0,   '\0'),
                    new KeyEntry(0,   '\0'),
                },
                {
                    new KeyEntry(VK_9,       '(', true), // Shift+9
                    new KeyEntry(VK_0,       ')', true), // Shift+0
                    new KeyEntry(VK_OEM_4,   '['),
                    new KeyEntry(0,          '\0'),
                },
                {
                    new KeyEntry(VK_OEM_6, ']'),
                    new KeyEntry(VK_OEM_5, '\\'),
                    new KeyEntry(VK_OEM_3, '`'),
                    new KeyEntry(0,        '\0'),
                },
            }
        };

        private Thread? _thread;
        private volatile bool _running;

        // Additive to the read path below, not a replacement for it: still fed the same PadState
        // ProcessSticks/ProcessButtons/ProcessKeyboardMode already consume, and only submits it to
        // a virtual controller as well when AppSettings.Instance.UseHidHide is on and the drivers
        // are ready. See GamepadPassthroughController's own comment for why the Guide button can
        // never reach that virtual pad.
        private readonly GamepadPassthroughController _passthrough;

        public ControllerPoller()
            : this(new InputEmulator(new Win32DesktopInput()))
        {
        }

        internal ControllerPoller(InputEmulator input)
            : this(new GamepadPassthroughController(), input)
        {
        }

        internal ControllerPoller(GamepadPassthroughController passthrough, InputEmulator input)
        {
            _passthrough = passthrough;
            _input = input;
        }

        private readonly InputEmulator _input;
        private readonly XInputPadReader _xinput = new(TimeProvider.System);

        private const int SlowScrollIntervalMs = 200;
        private const int FastScrollIntervalMs = 20;

        private long _lastScrollTick;
        private long _lastHorizontalScrollTick;

        // Written on the poll thread, read from the UI thread (KeyboardOverlayForm's paint timer).
        // Same cross-thread-visibility reasoning as IsControllerConnected below, so volatile here too.
        private volatile bool _keyboardMode;
        private volatile int _keyboardLayer;
        private volatile int _currentSector;

        private bool _ltWasDown;
        private bool _rtWasDown;

        public bool KeyboardMode => _keyboardMode;
        public int KeyboardLayer => _keyboardLayer;
        public int CurrentSector => _currentSector;

        // Written on the poll thread, read from the UI thread by Settings' status row. Plain
        // bool/string assignment is atomic in .NET, so this doesn't need a lock for a
        // best-effort status display.
        public volatile bool IsControllerConnected;
        public string ControllerStatusText { get; private set; } = "No controller detected";

        public static KeyEntry[,,] KeyboardLayout => Daisywheel;
        private static int StickDeadZone => AppSettings.Instance.StickDeadZone;
        private static int TouchpadSpeed => AppSettings.Instance.TouchpadSpeed;
        private static int ScrollDeadZone => AppSettings.Instance.ScrollDeadZone;
        private static float StickSensitivity => AppSettings.Instance.StickSensitivity;
        private static int KeyboardDeadZone => AppSettings.Instance.KeyboardDeadZone;
        private static float StickAccelPower => AppSettings.Instance.StickAccelPower;
        private static float StickRampSeconds => AppSettings.Instance.StickRampSeconds;

        private volatile int _slotIndex;
        public int SlotIndex => _slotIndex;

        private PadButtons _prevButtons;
        private static bool _watching;
        private static bool _streaming;

        private PadButtons _lastRawButtons;
        private PadButtons _stableButtons;
        private readonly HeldButtonGate _heldOverButtons = new();

        public void Start()
        {
            if (_running) return;

            _running = true;
            _thread = new Thread(Loop)
            {
                IsBackground = true,
                Name = "ControllerPoller"
            };
            _thread.Start();
        }

        public void Stop()
        {
            _running = false;
            _thread?.Join();
        }

        // Loop()'s finally block already disconnects/uncloaks via _passthrough.Shutdown() by the
        // time Stop() returns - this only releases the underlying ViGEmClient object itself.
        public void Dispose() => _passthrough.Dispose();

        // For Settings' "Use HidHide" toggle: whether it should currently be
        // enabled/checked, and a way to tell the poller a fresh install just succeeded.
        public Task<DriverStatus> DetectDriverStatusAsync(CancellationToken ct = default) =>
            _passthrough.DetectDriverStatusAsync(ct);

        public Task RefreshDriverStatusAsync() => Task.Run(_passthrough.RefreshDriverStatus);

        internal static class FullscreenHelper
        {
            [DllImport("user32.dll")]
            private static extern IntPtr GetForegroundWindow();

            [DllImport("user32.dll")]
            private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

            [DllImport("user32.dll")]
            private static extern IntPtr GetDesktopWindow();

            [DllImport("user32.dll")]
            private static extern IntPtr GetShellWindow();

            [DllImport("user32.dll")]
            private static extern int GetWindowThreadProcessId(IntPtr hWnd, out int lpdwProcessId);

            // A char[] marshals by pinning directly; StringBuilder here would make the interop
            // marshaler allocate its own native buffer and copy through it twice (once into that
            // buffer, once into the StringBuilder) for no benefit, since the result is only ever
            // read once immediately below.
            [DllImport("user32.dll", CharSet = CharSet.Unicode)]
            private static extern int GetWindowText(IntPtr hWnd, char[] lpString, int nMaxCount);

            [DllImport("user32.dll")]
            private static extern int GetWindowTextLength(IntPtr hWnd);

            // Windows 10+ pads a normal (bordered/maximized) window's GetWindowRect with an
            // invisible ~8px resize border on each side that isn't visually real - a maximized
            // video player can be genuinely edge-to-edge on screen while GetWindowRect reports it
            // as a few pixels short/over. DWMWA_EXTENDED_FRAME_BOUNDS returns the actual visual
            // bounds instead, which lines up exactly with the monitor for both true borderless
            // fullscreen (a WS_POPUP window with no frame, where this already equals GetWindowRect)
            // and a maximized bordered window (where it doesn't).
            [DllImport("dwmapi.dll")]
            private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

            private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

            [StructLayout(LayoutKind.Sequential)]
            private struct RECT
            {
                public int Left;
                public int Top;
                public int Right;
                public int Bottom;
            }

            private static readonly IntPtr DesktopHandle = GetDesktopWindow();
            private static readonly IntPtr ShellHandle = GetShellWindow();

            private static long _lastCheckTick;
            private static bool _lastResult;

            // None of this (monitor layout, foreground process, window title) changes meaningfully
            // within a 100ms window, but the poll loop calls this every ~8ms - re-enumerating
            // monitors and resolving the process/title that often is pure waste for state that's
            // effectively static between checks. Throttling to 10Hz cuts the expensive path by
            // ~12x with no perceptible change in responsiveness.
            private const int RecheckIntervalMs = 100;

            public static bool IsBlockedFullscreen()
            {
                long now = Environment.TickCount64;
                if (now - _lastCheckTick < RecheckIntervalMs)
                    return _lastResult;
                _lastCheckTick = now;
                _lastResult = ComputeIsBlockedFullscreen();
                return _lastResult;
            }

            private static bool ComputeIsBlockedFullscreen()
            {
                IntPtr hWnd = GetForegroundWindow();
                if (hWnd == IntPtr.Zero || hWnd == DesktopHandle || hWnd == ShellHandle)
                    return false;

                bool gotExtendedBounds = DwmGetWindowAttribute(
                    hWnd, DWMWA_EXTENDED_FRAME_BOUNDS, out RECT rect, Marshal.SizeOf<RECT>()) == 0;

                if (!gotExtendedBounds && !GetWindowRect(hWnd, out rect))
                    return false;

                // A window only counts as fullscreen if it exactly matches one monitor's bounds on
                // all four sides. Matching width/height alone (the old check) can also be true for
                // a window that's merely the right size but positioned elsewhere - e.g. straddling
                // a monitor boundary - without actually covering any single monitor.
                if (!MatchesAScreen(rect))
                {
                    _watching = false;
                    _streaming = false;
                    return false;
                }

                _ = GetWindowThreadProcessId(hWnd, out int pid);
                string? name = ProcessNames.NameFor(hWnd, pid);
                if (name != null)
                {
                    string title = GetWindowTitle(hWnd);

                    _watching = ContainsAny(name, AppSettings.Instance.WatchedProcessNames);

                    // 'S' (Skip Intro) only makes sense for actual streaming services: something
                    // with a known streaming service name in its title, or Edge itself, since the
                    // Windows Store apps for these services (Netflix, Prime Video, Disney+, etc.)
                    // are usually just an Edge WebView host under the hood and don't always surface
                    // the service name in their title.
                    _streaming = name.Contains("edge", StringComparison.OrdinalIgnoreCase) ||
                        ContainsAny(title, AppSettings.Instance.StreamingServiceNames);

                    if (_watching || _streaming)
                        return false;
                }

                _watching = false;
                _streaming = false;
                return true;
            }

            private static readonly ProcessNameCache ProcessNames = new();

            private static bool MatchesAScreen(RECT rect)
            {
                foreach (var screen in Screen.AllScreens)
                {
                    var bounds = screen.Bounds;
                    if (rect.Left == bounds.Left && rect.Top == bounds.Top && rect.Right == bounds.Right && rect.Bottom == bounds.Bottom)
                        return true;
                }

                return false;
            }

            // Case-insensitive, ignoring blank and padded entries, without allocating per entry.
            private static bool ContainsAny(string text, List<string> needles)
            {
                foreach (string needle in needles)
                {
                    var trimmed = needle.AsSpan().Trim();
                    if (!trimmed.IsEmpty && text.AsSpan().Contains(trimmed, StringComparison.OrdinalIgnoreCase))
                        return true;
                }

                return false;
            }

            private static string GetWindowTitle(IntPtr hWnd)
            {
                int length = GetWindowTextLength(hWnd);
                if (length == 0)
                    return string.Empty;

                var buffer = new char[length + 1];
                int copied = GetWindowText(hWnd, buffer, buffer.Length);
                return copied > 0 ? new string(buffer, 0, copied) : string.Empty;
            }
        }

        private void Loop()
        {
            // Constructed and used only on this thread: SDL's event queue is meant to be pumped
            // consistently from a single thread for its whole lifetime.
            using var sdlPadReader = new Sdl2PadReader(TimeProvider.System);
            using var pacer = new PollPacer();

            try
            {
                while (_running)
                {
                    int sleepMs;
                    try
                    {
                        sleepMs = RunTick(sdlPadReader);
                    }
                    catch (Exception ex)
                    {
                        RecoverFromTickFault(ex);
                        sleepMs = PollIntervalMs;
                    }

                    pacer.Wait(sleepMs);
                }
            }
            finally
            {
                // Covers both the ordinary clean-exit path (Stop() joins this thread, so this runs
                // before Stop() returns) and a same-process crash on this thread - see
                // GamepadPassthroughController.Shutdown for why a crash/force-kill that skips this
                // entirely is still recovered from, on the next launch.
                _passthrough.Shutdown();
            }
        }

        private const int PollIntervalMs = 8;
        private const int FullscreenPollIntervalMs = 100;
        // With no controller there's nothing to be smooth for; a new one is still read within this.
        private const int IdlePollIntervalMs = 100;

        // Returns how long to wait before the next tick.
        private int RunTick(Sdl2PadReader sdlPadReader)
        {
            bool blockedFullscreen = FullscreenHelper.IsBlockedFullscreen();
            _passthrough.SetFullscreenSuspended(blockedFullscreen);
            sdlPadReader.SetLightbar(ControllerLights.ColorFor(ControllerLights.ComputeMode(blockedFullscreen, _keyboardMode)));

            if (blockedFullscreen)
            {
                StandDownForFullscreen();
                return FullscreenPollIntervalMs;
            }

            // Keeps SDL's device state (event queue drained, a newly available controller
            // opened) current every tick, independent of which source ends up supplying the
            // frame below - see the comment on PumpEvents() for why that independence matters.
            sdlPadReader.PumpEvents();
            sdlPadReader.SetPlayerLights(ControllerLights.PlayerLightsFor(sdlPadReader.Battery));

            PadSource source = _xinput.TryReadAny(out var pad, _passthrough.VirtualPadXInputSlots) ? PadSource.XInput
                : sdlPadReader.TryGetLatest(out pad) ? PadSource.Sdl
                : PadSource.None;
            bool gotPad = source != PadSource.None;

            IsControllerConnected = gotPad;
            UpdateStatusText(source);
            _connection.Observe(source, _xinput.LastSlot, sdlPadReader.CurrentDeviceIdentity, sdlPadReader.ConnectionSerial);

            if (gotPad)
            {
                pad.Buttons = FilterButtons(pad.Buttons);

                if (_keyboardMode)
                {
                    ProcessKeyboardMode(pad);
                }
                else
                {
                    ProcessSticks(pad);
                    ProcessTouchpad(pad);
                }

                ProcessButtons(pad);
            }
            else
            {
                HandlePadLost();
            }

            _passthrough.Tick(pad, gotPad, _connection.Identity, _connection.Serial);
            return gotPad ? PollIntervalMs : IdlePollIntervalMs;
        }

        private readonly RepeatingFaultThrottle _tickFaults = new(TimeProvider.System);

        // A throwing tick (including a KeyboardModeChanged or notice subscriber, which run here)
        // must not end the thread. Held buttons are suppressed until released, since the failed
        // tick never recorded them and would otherwise fire their press again on every tick.
        private void RecoverFromTickFault(Exception ex)
        {
            if (_tickFaults.Record(ex) is { } report)
            {
                if (report.IsNew)
                    AppLog.Default.Error("ControllerPoller: a poll tick failed; releasing held input and carrying on", ex);
                else
                    AppLog.Default.Error($"ControllerPoller: the same poll tick failure repeated {report.Repeats} more times");
            }

            try
            {
                ReleaseHeldInput();
                _heldOverButtons.SuppressHeld();
            }
            catch (Exception releaseEx)
            {
                AppLog.Default.Warning("ControllerPoller: failed to release held input after a tick failure", releaseEx);
            }
        }

        // The game gets the pad to itself: passthrough stands down through its normal tick, and
        // nothing this app pressed stays held while it isn't reading the pad.
        internal void StandDownForFullscreen()
        {
            PauseInput();

            _passthrough.SetFullscreenSuspended(true);
            _passthrough.Tick(default, gotPad: false, _connection.Identity, _connection.Serial);
        }

        internal void HandlePadLost() => PauseInput();

        // Whatever was held or moving when input stopped must not resume as a press, a click or a
        // stick already at full ramp speed; triggers count as held until released.
        private void PauseInput()
        {
            ReleaseHeldInput();
            _heldOverButtons.SuppressHeld();
            _lastRawButtons = PadButtons.None;
            _stableButtons = PadButtons.None;
            _prevButtons = PadButtons.None;
            _ltWasDown = true;
            _rtWasDown = true;
            _moveHoldStartTick = 0;
            _dxRemainder = 0;
            _dyRemainder = 0;
        }

        internal PadButtons FilterButtons(PadButtons raw) => DebounceButtons(_heldOverButtons.Filter(raw));

        private void ReleaseHeldInput()
        {
            _touchpadMouse.Reset();
            _touchpadHoldsLeft = false;
            _input.SetLeftButtonState(false);
        }

        // Interpolating ControllerStatusText fresh every tick (the loop runs at ~125Hz) allocated a
        // new string every 8ms for as long as a controller stayed connected; recomputing it only
        // when the underlying state actually changes turns that into an allocation on
        // connect/disconnect/slot-change instead.
        private PadSource? _lastStatusSource;
        private int _lastStatusSlot = -1;

        private readonly PadConnectionTracker _connection = new();

        private void UpdateStatusText(PadSource source)
        {
            int slot = source == PadSource.XInput ? _xinput.LastSlot : -1;

            if (source == _lastStatusSource && slot == _lastStatusSlot)
                return;

            _lastStatusSource = source;
            _lastStatusSlot = slot;

            ControllerStatusText = source switch
            {
                PadSource.XInput => $"XInput controller · slot {slot}",
                PadSource.Sdl => "Controller connected (SDL)",
                _ => "No controller detected"
            };
        }

        // Some pads (PS4/PS5 over Bluetooth especially, or when routed through a virtual XInput
        // layer like Steam's controller support) can report a single-tick flicker on the D-pad or
        // face buttons - a spurious blip that WasPressed() would otherwise read as a real press
        // and fire on. Requiring two consecutive identical polls before trusting a reading filters
        // that out while staying well under human reaction time (this loop runs at ~125Hz).
        internal PadButtons DebounceButtons(PadButtons raw)
        {
            if (raw == _lastRawButtons)
                _stableButtons = raw;
            _lastRawButtons = raw;
            return _stableButtons;
        }

        // Sub-pixel remainder carried between ticks so slow movement (< 1px/tick) accumulates into
        // whole pixels smoothly instead of being truncated away every frame (which reads as
        // stair-stepped, laggy motion at low stick deflection).
        private double _dxRemainder;
        private double _dyRemainder;

        // Tick the stick was last pushed past the deadzone after being centered; 0 while centered.
        // Drives the hold-time speed ramp below.
        private long _moveHoldStartTick;

        private void ProcessSticks(PadState pad)
        {
            var lx = pad.LeftThumbX;
            var ly = pad.LeftThumbY;
            var mag = StickMagnitude(lx, ly);
            if (mag < StickDeadZone)
            {
                _dxRemainder = 0;
                _dyRemainder = 0;
                _moveHoldStartTick = 0;
                HandleScroll(pad.RightThumbY, pad.RightThumbX);
                return;
            }

            long now = Environment.TickCount64;
            if (_moveHoldStartTick == 0)
                _moveHoldStartTick = now;
            double heldSeconds = (now - _moveHoldStartTick) / 1000.0;

            var normX = lx / 32767.0;
            var normY = ly / 32767.0;

            var normMag = mag / 32767.0;
            if (normMag > 1.0) normMag = 1.0;

            var curvedMag = Math.Pow(normMag, StickAccelPower);
            var holdRamp = ComputeHoldRamp(heldSeconds, StickRampSeconds);

            var factor = curvedMag * holdRamp * StickSensitivity * 1000.0;

            double exactDx = normX * factor + _dxRemainder;
            double exactDy = -normY * factor + _dyRemainder;

            var dx = (int)exactDx;
            var dy = (int)exactDy;

            _dxRemainder = exactDx - dx;
            _dyRemainder = exactDy - dy;

            if (dx != 0 || dy != 0)
                _input.MoveMouse(dx, dy);

            HandleScroll(pad.RightThumbY, pad.RightThumbX);
        }

        // In double: a fully deflected corner (-32768, -32768) overflows int to a negative sum.
        internal static double StickMagnitude(short x, short y) => Math.Sqrt(((double)x * x) + ((double)y * y));

        private readonly TouchpadMouse _touchpadMouse = new(TimeProvider.System);
        private bool _touchpadHoldsLeft;

        private void ProcessTouchpad(PadState pad)
        {
            var touch = _touchpadMouse.Update(
                pad.TouchActive, pad.TouchX, pad.TouchY, (pad.Buttons & PadButtons.TouchpadClick) != 0, TouchpadSpeed);
            _touchpadHoldsLeft = touch.HoldLeft;

            if (touch.Dx != 0 || touch.Dy != 0)
                _input.MoveMouse(touch.Dx, touch.Dy);

            switch (touch.Click)
            {
                case TouchClick.Left:
                    _input.LeftClick();
                    break;
                case TouchClick.Right:
                    _input.RightClick();
                    break;
                case TouchClick.None:
                    break;
            }
        }

        // Logistic (S-curve) ramp: near 0 right after the stick leaves the deadzone, crosses the
        // midpoint at rampSeconds/2, and is near 1 by rampSeconds - a quick tap stays slow and
        // precise, while a sustained push reaches full speed quickly rather than snapping there
        // instantly. rampSeconds <= 0 disables the ramp (always full speed, prior behavior).
        internal static double ComputeHoldRamp(double heldSeconds, double rampSeconds)
        {
            if (rampSeconds <= 0.01)
                return 1.0;

            double midpoint = rampSeconds / 2.0;
            double steepness = Math.Log(19.0) / midpoint; // spans ~5% -> ~95% across [0, rampSeconds]
            return 1.0 / (1.0 + Math.Exp(-steepness * (heldSeconds - midpoint)));
        }

        private const int WheelNotch = 120; // one wheel "notch" in Windows

        private void HandleScroll(short ry, short rx)
        {
            long now = Environment.TickCount64;
            int vertical = TryScroll(ry, now, ref _lastScrollTick);
            if (vertical != 0)
                _input.MouseWheelVertical(vertical);

            int horizontal = TryScroll(rx, now, ref _lastHorizontalScrollTick);
            if (horizontal != 0)
                _input.MouseWheelHorizontal(horizontal);
        }

        // Returns the wheel delta to send now, or 0.
        private static int TryScroll(int axisValue, long now, ref long lastTick)
        {
            int abs = axisValue == short.MinValue ? short.MaxValue : Math.Abs(axisValue);
            if (abs < ScrollDeadZone)
                return 0;

            double norm = (abs - ScrollDeadZone) / (32767.0 - ScrollDeadZone);
            if (norm < 0) norm = 0;
            if (norm > 1) norm = 1;

            int interval = (int)(SlowScrollIntervalMs - norm * (SlowScrollIntervalMs - FastScrollIntervalMs));
            if (now - lastTick < interval)
                return 0;

            lastTick = now;

            int delta = (int)(WheelNotch * norm);
            if (delta == 0) delta = WheelNotch;

            return axisValue > 0 ? delta : -delta;
        }

        public event Action<bool>? KeyboardModeChanged;

        public event Action<PassthroughNotice>? PassthroughNoticeRaised
        {
            add => _passthrough.NoticeRaised += value;
            remove => _passthrough.NoticeRaised -= value;
        }

        // Enum.HasFlag boxes both the receiver and the argument on every call; at this loop's
        // ~125Hz cadence with a dozen-plus flags checked per tick, that's a steady stream of
        // avoidable GC pressure. PadButtons is a plain int-backed [Flags] enum, so a bitwise
        // check is both cheaper and allocation-free.
        private bool WasPressed(PadButtons current, PadButtons flag) =>
            (current & flag) != 0 && (_prevButtons & flag) == 0;

        private void ProcessButtons(PadState pad)
        {
            var buttons = pad.Buttons;

            bool A_down = (buttons & PadButtons.A) != 0;

            bool B_pressed = WasPressed(buttons, PadButtons.B);
            bool X_pressed = WasPressed(buttons, PadButtons.X);
            bool Y_pressed = WasPressed(buttons, PadButtons.Y);
            bool LB_pressed = WasPressed(buttons, PadButtons.LeftShoulder);
            bool RB_pressed = WasPressed(buttons, PadButtons.RightShoulder);
            bool Back_pressed = WasPressed(buttons, PadButtons.Back);
            bool Start_pressed = WasPressed(buttons, PadButtons.Start);
            bool Up_pressed = WasPressed(buttons, PadButtons.DPadUp);
            bool Down_pressed = WasPressed(buttons, PadButtons.DPadDown);
            bool Left_pressed = WasPressed(buttons, PadButtons.DPadLeft);
            bool Right_pressed = WasPressed(buttons, PadButtons.DPadRight);
            bool LS_pressed = WasPressed(buttons, PadButtons.LeftThumb);
            bool RS_pressed = WasPressed(buttons, PadButtons.RightThumb);

            if (LS_pressed)
            {
                _keyboardMode = !_keyboardMode;
                KeyboardModeChanged?.Invoke(_keyboardMode);
            }

            const ushort VK_BACK = 0x08; // Backspace
            const ushort VK_ESCAPE = 0x1B;
            const ushort VK_RETURN = 0x0D;
            const ushort VK_LEFT = 0x25;
            const ushort VK_UP = 0x26;
            const ushort VK_RIGHT = 0x27;
            const ushort VK_DOWN = 0x28;
            const ushort VK_MEDIA_PLAY_PAUSE = 0xB3;
            const ushort VK_CTRL = 0x11;
            const ushort VK_SHIFT = 0x10;

            // Driven directly off current state (not edges) so the button can never get stuck
            // down if keyboard mode is toggled while A or the touchpad is still held.
            _input.SetLeftButtonState(!_keyboardMode && (A_down || _touchpadHoldsLeft));

            if (!_keyboardMode)
            {
                if (B_pressed && !_streaming)
                    _input.SendKey(VK_BACK);
                else if (B_pressed)
                    _input.SendKey(VK_ESCAPE);

                // 'S' is only bound to Skip Intro for actual streaming services - see the
                // _streaming computation in FullscreenHelper. Generic fullscreen apps (VLC, Steam,
                // Explorer, etc.) fall through to a plain right-click instead.
                if (X_pressed && _streaming)
                    _input.SendKey(VK_S);
                else if (X_pressed)
                    _input.RightClick();

                if (Y_pressed)
                    _input.SendKey(VK_MEDIA_PLAY_PAUSE);

                if (_watching || _streaming)
                {
                    if (Up_pressed)
                        _input.SendKey(VK_UP);
                    if (Down_pressed)
                        _input.SendKey(VK_DOWN);
                    if (Left_pressed)
                        _input.SendKey(VK_LEFT);
                    if (Right_pressed)
                        _input.SendKey(VK_RIGHT);

                    // Netflix's documented shortcut for previous/next episode.
                    if (LB_pressed)
                        _input.SendKeyWithModifier(VK_SHIFT, VK_LEFT);
                    if (RB_pressed)
                        _input.SendKeyWithModifier(VK_SHIFT, VK_RIGHT);
                }
                if (RS_pressed && !A_down)
                    _input.LeftClickWithModifier(VK_CTRL);
            }

            if (Start_pressed)
                _input.SendKey(VK_RETURN);

            if (Back_pressed)
                _input.SendKey(VK_ESCAPE);

            _prevButtons = buttons;
        }

        private void ProcessKeyboardMode(PadState pad)
        {
            _touchpadMouse.Reset();
            _touchpadHoldsLeft = false;
            var buttons = pad.Buttons;

            const byte TriggerPressThreshold = 160;
            const byte TriggerReleaseThreshold = 120;

            bool LB_pressed = WasPressed(buttons, PadButtons.LeftShoulder);
            bool RB_pressed = WasPressed(buttons, PadButtons.RightShoulder);

            byte LT_raw = pad.LeftTrigger;
            byte RT_raw = pad.RightTrigger;

            // down state with simple hysteresis: once down, stay down until clearly released
            bool LT_down = _ltWasDown
                ? (LT_raw > TriggerReleaseThreshold)
                : (LT_raw >= TriggerPressThreshold);

            bool RT_down = _rtWasDown
                ? (RT_raw > TriggerReleaseThreshold)
                : (RT_raw >= TriggerPressThreshold);

            // edge: only once per pull above the press threshold
            bool LT_pressed = LT_down && !_ltWasDown;
            bool RT_pressed = RT_down && !_rtWasDown;

            _ltWasDown = LT_down;
            _rtWasDown = RT_down;

            bool A_pressed = WasPressed(buttons, PadButtons.A);
            bool B_pressed = WasPressed(buttons, PadButtons.B);
            bool X_pressed = WasPressed(buttons, PadButtons.X);
            bool Y_pressed = WasPressed(buttons, PadButtons.Y);

            bool Left_pressed = WasPressed(buttons, PadButtons.DPadLeft);
            bool Right_pressed = WasPressed(buttons, PadButtons.DPadRight);

            const ushort VK_BACK = 0x08;
            const ushort VK_SPACE = 0x20;
            const ushort VK_PERIOD = 0xBE;

            if (Left_pressed || LT_pressed)
            {
                _keyboardLayer = (_keyboardLayer + 2) % 3;   // backwards (0<-1<-2)
                _slotIndex = 0;
            }
            if (Right_pressed || RT_pressed)
            {
                _keyboardLayer = (_keyboardLayer + 1) % 3;   // forwards (0->1->2)
                _slotIndex = 0;
            }

            if (X_pressed)
            {
                // Backspace
                _input.SendKey(VK_BACK);
                return;
            }

            if (Y_pressed)
            {
                // Space
                _input.SendKey(VK_SPACE);
                return;
            }

            if (B_pressed)
            {
                // Period
                _input.SendKey(VK_PERIOD);
                return;
            }

            bool typePressed = A_pressed || WasPressed(buttons, PadButtons.TouchpadClick);
            int sector = GetSector(pad.LeftThumbX, pad.LeftThumbY);
            var touch = pad.TouchActive ? MapTouchToWheel(pad.TouchX, pad.TouchY, _keyboardLayer) : null;

            if (TouchDrivesWheel(sector, touch))
            {
                var selection = touch!.Value;
                _currentSector = selection.Sector;
                _slotIndex = selection.Ring;
                if (typePressed)
                    EmitDaisywheelKey(_keyboardLayer, selection.Sector, selection.Ring);
                return;
            }

            if (sector < 0)
                return;
            int count = GetEntryCount(_keyboardLayer, sector);
            if (count == 0)
                return;

            if (RB_pressed)
                _slotIndex = (_slotIndex + 1) % count;

            if (LB_pressed)
                _slotIndex = (_slotIndex - 1 + count) % count;

            if (typePressed)
                EmitDaisywheelKey(_keyboardLayer, sector, _slotIndex);
        }

        // The stick wins while pushed, so a finger resting on the pad only to press it can't move the selection.
        internal static bool TouchDrivesWheel(int stickSector, WheelSelection? touch) =>
            stickSector < 0 && touch is not null;

        private void EmitDaisywheelKey(int layer, int sector, int index)
        {
            var entry = Daisywheel[layer, sector, index];
            if (entry.Vk == 0)
                return;
            if (entry.HasMod)
            {
                const ushort VK_SHIFT = 0x10;
                _input.SendKeyWithModifier(VK_SHIFT, entry.Vk);
                return;
            }
            _input.SendKey(entry.Vk);
        }
        private static int GetEntryCount(int layer, int sector)
        {
            int count = 0;
            for (int i = 0; i < 4; i++)
            {
                if (Daisywheel[layer, sector, i].Vk != 0)
                    count++;
            }
            return count;
        }
        private int GetSector(short lx, short ly)
        {
            int sector = ComputeSector(lx, ly, KeyboardDeadZone);
            // Clamped before the sector is published so the overlay never highlights an empty slot.
            if (sector >= 0)
                _slotIndex = ClampSlot(_keyboardLayer, sector, _slotIndex);
            _currentSector = sector;
            return sector;
        }

        // A ring picked in a fuller sector (or on the touchpad) carries over to the sector the stick moves to.
        internal static int ClampSlot(int layer, int sector, int slot)
        {
            int count = GetEntryCount(layer, sector);
            return count == 0 ? 0 : Math.Min(slot, count - 1);
        }

        // Inner part of the touchpad, as a fraction of its half-size, that selects nothing.
        private const float TouchCentreDeadZone = 0.2f;

        // The touchpad's centre is the wheel's centre: the finger's angle picks the sector the same
        // way the stick does, and its distance out picks the ring.
        internal static WheelSelection? MapTouchToWheel(float x, float y, int layer)
        {
            float nx = (x - 0.5f) * 2f;
            float ny = (0.5f - y) * 2f;
            double radius = Math.Min(1.0, Math.Sqrt(nx * nx + ny * ny));
            if (radius < TouchCentreDeadZone)
                return null;

            int sector = ComputeSector((short)(nx * short.MaxValue), (short)(ny * short.MaxValue), deadZone: 0);
            int count = GetEntryCount(layer, sector);
            if (count == 0)
                return null;

            int ring = (int)((radius - TouchCentreDeadZone) / (1 - TouchCentreDeadZone) * count);
            return new WheelSelection(sector, Math.Min(ring, count - 1));
        }

        // Pure geometry, split out from GetSector so it's directly testable: KeyboardDeadZone
        // reads AppSettings.Instance, which lazily loads the real settings.json from disk on
        // first touch - not something a test should depend on. Mirrors the same
        // pure-core-plus-stateful-wrapper split as ComputeHoldRamp below.
        internal static int ComputeSector(short lx, short ly, int deadZone)
        {
            long x = lx;
            long y = ly;

            // In long: a fully deflected corner overflows int (see StickMagnitude).
            long magSq = (x * x) + (y * y);
            if (magSq < (long)deadZone * deadZone)
                return -1;

            double angleRad = Math.Atan2(y, x);
            double angleDeg = angleRad * (180.0 / Math.PI);

            // Now angleDeg is standard: 0° = right, 90° = up, 180° = left, 270° = down

            // Center sector 0 at 90° (up), then wrap to [0, 360)
            angleDeg -= 90.0;
            if (angleDeg < 0) angleDeg += 360.0;

            // Each sector is 45°. Using +22.5 shifts to center each sector.
            int sector = (int)Math.Floor((angleDeg + 22.5) / 45.0);

            if (sector < 0 || sector >= 8)
                sector = 0;

            return sector;
        }
    }
}
