using System.Diagnostics;
using System.Runtime.InteropServices;
using Vortice.XInput;

namespace ControllerMagic
{
    internal class ControllerPoller
    {
        private Thread? _thread;
        private bool _running;

        private const int MinIntervalMs = 200;
        private const int MaxIntervalMs = 20;

        private long _lastScrollTick;
        private long _lastHorizontalScrollTick;

        private bool _keyboardMode;
        private int _keyboardLayer;
        private int _currentSector;

        public bool KeyboardMode => _keyboardMode;
        public int KeyboardLayer => _keyboardLayer;
        public int CurrentSector => _currentSector;
        public static KeyEntry[,,] KeyboardLayout => Daisywheel;
        private int StickDeadZone => AppSettings.Instance.StickDeadZone;
        private int ScrollDeadZone => AppSettings.Instance.ScrollDeadZone;
        private float StickSensitivity => AppSettings.Instance.StickSensitivity;
        private int KeyboardDeadZone => AppSettings.Instance.KeyboardDeadZone;
        private float StickAccelPower => AppSettings.Instance.StickAccelPower;

        private int _slotIndex;
        public int SlotIndex => _slotIndex;

        private GamepadButtons _prevButtons;


        public void Start()
        {
            if (_running) return;

            if (!XInput.GetCapabilities(0, DeviceQueryType.Any, out _))
                return;

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
        }

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

            public static bool IsBlockedFullscreen()
            {
                IntPtr hWnd = GetForegroundWindow();
                if (hWnd == IntPtr.Zero || hWnd == DesktopHandle || hWnd == ShellHandle)
                    return false;

                if (!GetWindowRect(hWnd, out RECT rect))
                    return false;

                var screen = Screen.FromHandle(hWnd);
                var bounds = screen.Bounds;

                int width = rect.Right - rect.Left;
                int height = rect.Bottom - rect.Top;

                bool isFullscreen = width == bounds.Width && height == bounds.Height;
                if (!isFullscreen)
                    return false;

                GetWindowThreadProcessId(hWnd, out int pid);
                try
                {
                    using var proc = Process.GetProcessById(pid);
                    string name = proc.ProcessName.ToLowerInvariant();

                    if (name.Contains("firefox") || name.Contains("vlc") || name.Contains("chrome") || name.Contains("explorer") || name.Contains("edge"))
                        return false;
                }
                catch
                {
                }

                return true;
            }
        }


        private void Loop()
        {
            while (_running)
            {
                if (FullscreenHelper.IsBlockedFullscreen())
                {
                    Thread.Sleep(100);
                    continue;
                }

                if (XInput.GetState(0, out var state))
                {
                    var pad = state.Gamepad;



                    if (_keyboardMode)
                        ProcessKeyboardMode(pad);
                    else
                        ProcessSticks(pad);

                    ProcessButtons(pad);
                }

                Thread.Sleep(8);
            }
        }

        private void ProcessSticks(Gamepad pad)
        {
            var lx = pad.LeftThumbX;
            var ly = pad.LeftThumbY;
            var mag = Math.Sqrt(lx * lx + ly * ly);
            if (mag < StickDeadZone)
            {
                HandleScroll(pad.RightThumbY, pad.RightThumbX);
                return;
            }

            var normX = lx / 32767.0;
            var normY = ly / 32767.0;

            var normMag = mag / 32767.0;
            if (normMag > 1.0) normMag = 1.0;

            var curvedMag = Math.Pow(normMag, StickAccelPower);

            var factor = curvedMag * StickSensitivity * 1000.0;

            var dx = (int)(normX * factor);
            var dy = (int)(-normY * factor);
            if (dx != 0 || dy != 0)
                InputEmulator.MoveMouse(dx, dy);

            HandleScroll(pad.RightThumbY, pad.RightThumbX);
        }


        private void HandleScroll(short ry, short rx)
        {
            long now = Environment.TickCount64;

            int v = ry;
            int absV = v == short.MinValue ? short.MaxValue : Math.Abs(v);
            if (absV >= ScrollDeadZone)
            {
                double normV = (absV - ScrollDeadZone) / (32767.0 - ScrollDeadZone);
                if (normV < 0) normV = 0;
                if (normV > 1) normV = 1;

                int intervalV = (int)(MinIntervalMs - normV * (MinIntervalMs - MaxIntervalMs));

                if (now - _lastScrollTick >= intervalV)
                {
                    _lastScrollTick = now;

                    // 120 is one wheel "notch" in Windows
                    int baseStep = 120;

                    int delta = (int)(baseStep * normV);
                    if (delta == 0) delta = baseStep;

                    int signedDelta = v > 0 ? delta : -delta;

                    InputEmulator.MouseWheelVertical(signedDelta);
                }
            }

            int h = rx;
            int absH = h == short.MinValue ? short.MaxValue : Math.Abs(h);
            if (absH >= ScrollDeadZone)
            {
                double normH = (absH - ScrollDeadZone) / (32767.0 - ScrollDeadZone);
                if (normH < 0) normH = 0;
                if (normH > 1) normH = 1;

                int intervalH = (int)(MinIntervalMs - normH * (MinIntervalMs - MaxIntervalMs));

                if (now - _lastHorizontalScrollTick >= intervalH)
                {
                    _lastHorizontalScrollTick = now;

                    int baseStep = 120;
                    int delta = (int)(baseStep * normH);
                    if (delta == 0) delta = baseStep;

                    int signedDelta = h < 0 ? -delta : delta;

                    InputEmulator.MouseWheelHorizontal(signedDelta);
                }
            }
        }


        private void ProcessButtons(Gamepad pad)
        {
            var buttons = pad.Buttons;

            bool A_down = buttons.HasFlag(GamepadButtons.A);
            bool B_down = buttons.HasFlag(GamepadButtons.B);
            bool X_down = buttons.HasFlag(GamepadButtons.X);
            bool Y_down = buttons.HasFlag(GamepadButtons.Y);
            bool LB_down = buttons.HasFlag(GamepadButtons.LeftShoulder);
            bool RB_down = buttons.HasFlag(GamepadButtons.RightShoulder);
            bool Back_down = buttons.HasFlag(GamepadButtons.Back);
            bool Start_down = buttons.HasFlag(GamepadButtons.Start);
            bool Up_down = buttons.HasFlag(GamepadButtons.DPadUp);
            bool Down_down = buttons.HasFlag(GamepadButtons.DPadDown);
            bool Left_down = buttons.HasFlag(GamepadButtons.DPadLeft);
            bool Right_down = buttons.HasFlag(GamepadButtons.DPadRight);
            bool LS_down = buttons.HasFlag(GamepadButtons.LeftThumb);
            bool RS_down = buttons.HasFlag(GamepadButtons.RightThumb);

            bool A_pressed = A_down && !_prevButtons.HasFlag(GamepadButtons.A);
            bool B_pressed = B_down && !_prevButtons.HasFlag(GamepadButtons.B);
            bool X_pressed = X_down && !_prevButtons.HasFlag(GamepadButtons.X);
            bool Y_pressed = Y_down && !_prevButtons.HasFlag(GamepadButtons.Y);
            bool LB_pressed = LB_down && !_prevButtons.HasFlag(GamepadButtons.LeftShoulder);
            bool RB_pressed = RB_down && !_prevButtons.HasFlag(GamepadButtons.RightShoulder);
            bool Back_pressed = Back_down && !_prevButtons.HasFlag(GamepadButtons.Back);
            bool Start_pressed = Start_down && !_prevButtons.HasFlag(GamepadButtons.Start);
            bool Up_pressed = Up_down && !_prevButtons.HasFlag(GamepadButtons.DPadUp);
            bool Down_pressed = Down_down && !_prevButtons.HasFlag(GamepadButtons.DPadDown);
            bool Left_pressed = Left_down && !_prevButtons.HasFlag(GamepadButtons.DPadLeft);
            bool Right_pressed = Right_down && !_prevButtons.HasFlag(GamepadButtons.DPadRight);
            bool LS_pressed = LS_down && !_prevButtons.HasFlag(GamepadButtons.LeftThumb);
            bool RS_pressed = RS_down && !_prevButtons.HasFlag(GamepadButtons.RightThumb);

            if (LS_pressed)
                _keyboardMode = !_keyboardMode;

            const ushort VK_BACK = 0x08; // Backspace
            const ushort VK_ESCAPE = 0x1B;
            const ushort VK_RETURN = 0x0D;
            const ushort VK_LEFT = 0x25;
            const ushort VK_UP = 0x26;
            const ushort VK_RIGHT = 0x27;
            const ushort VK_DOWN = 0x28;
            const ushort VK_MEDIA_PLAY_PAUSE = 0xB3;
            const ushort VK_MEDIA_PREV_TRACK = 0xB1;
            const ushort VK_MEDIA_NEXT_TRACK = 0xB0;
            const ushort VK_CTRL = 0x11;

            if (!_keyboardMode)
            {


                if (A_down)
                    InputEmulator.SetLeftButtonState(true);

                if (!A_down && _prevButtons.HasFlag(GamepadButtons.A))
                    InputEmulator.SetLeftButtonState(false);

                if (B_pressed)
                    InputEmulator.SendKey(VK_BACK);

                if (X_down)
                    InputEmulator.SetRightButtonState(true);

                if (!X_down && _prevButtons.HasFlag(GamepadButtons.X))
                    InputEmulator.SetRightButtonState(false);

                if (Y_pressed)
                    InputEmulator.SendKey(VK_MEDIA_PLAY_PAUSE);

                if (LB_pressed)
                    InputEmulator.SendKey(VK_MEDIA_PREV_TRACK);

                if (RB_pressed)
                    InputEmulator.SendKey(VK_MEDIA_NEXT_TRACK);
                if (Up_pressed)
                    InputEmulator.SendKey(VK_UP);
                if (Down_pressed)
                    InputEmulator.SendKey(VK_DOWN);
                if (Left_pressed)
                    InputEmulator.SendKey(VK_LEFT);
                if (Right_pressed)
                    InputEmulator.SendKey(VK_RIGHT);
                if (RS_pressed)
                {
                    InputEmulator.SendKey(VK_CTRL, true);
                    InputEmulator.LeftClick();
                    InputEmulator.SendKey(VK_CTRL, false);
                }
            }

            if (Start_pressed)
                InputEmulator.SendKey(VK_RETURN);

            if (Back_pressed)
                InputEmulator.SendKey(VK_ESCAPE);

            _prevButtons = buttons;


        }
        private void ProcessKeyboardMode(Gamepad pad)
        {
            var buttons = pad.Buttons;

            bool LB_down = buttons.HasFlag(GamepadButtons.LeftShoulder);
            bool RB_down = buttons.HasFlag(GamepadButtons.RightShoulder);
            bool LB_pressed = LB_down && !_prevButtons.HasFlag(GamepadButtons.LeftShoulder);
            bool RB_pressed = RB_down && !_prevButtons.HasFlag(GamepadButtons.RightShoulder);





            bool A_down = buttons.HasFlag(GamepadButtons.A);
            bool B_down = buttons.HasFlag(GamepadButtons.B);
            bool X_down = buttons.HasFlag(GamepadButtons.X);
            bool Y_down = buttons.HasFlag(GamepadButtons.Y);

            bool A_pressed = A_down && !_prevButtons.HasFlag(GamepadButtons.A);
            bool B_pressed = B_down && !_prevButtons.HasFlag(GamepadButtons.B);
            bool X_pressed = X_down && !_prevButtons.HasFlag(GamepadButtons.X);
            bool Y_pressed = Y_down && !_prevButtons.HasFlag(GamepadButtons.Y);

            bool Left_down = buttons.HasFlag(GamepadButtons.DPadLeft);
            bool Right_down = buttons.HasFlag(GamepadButtons.DPadRight);

            bool Left_pressed = Left_down && !_prevButtons.HasFlag(GamepadButtons.DPadLeft);
            bool Right_pressed = Right_down && !_prevButtons.HasFlag(GamepadButtons.DPadRight);

            const ushort VK_BACK = 0x08;
            const ushort VK_SPACE = 0x20;
            const ushort VK_PERIOD = 0xBE;

            if (Left_pressed)
                _keyboardLayer = (_keyboardLayer + 2) % 3;   // backwards (0<-1<-2)
            if (Right_pressed)
                _keyboardLayer = (_keyboardLayer + 1) % 3;   // forwards (0->1->2)

            if (X_pressed)
            {
                // Backspace
                InputEmulator.SendKey(VK_BACK);
                return;
            }

            if (Y_pressed)
            {
                // Space
                InputEmulator.SendKey(VK_SPACE);
                return;
            }

            if (B_pressed)
            {
                // Period
                InputEmulator.SendKey(VK_PERIOD);
                return;
            }

            int sector = GetSector(pad.LeftThumbX, pad.LeftThumbY);

            if (sector < 0)
                return;
            int count = GetEntryCount(_keyboardLayer, sector);
            if (count == 0)
                return;

            if (RB_pressed)
                _slotIndex = (_slotIndex + 1) % count;

            if (LB_pressed)
                _slotIndex = (_slotIndex - 1 + count) % count;

            if (A_pressed)
            {
                EmitDaisywheelKey(_keyboardLayer, sector, _slotIndex, true);
            }

        }

        private void EmitDaisywheelKey(int layer, int sector, int index, bool pressed)
        {
            if (!pressed)
                return;

            var entry = Daisywheel[layer, sector, index];
            if (entry.Vk == 0)
                return;
            System.Diagnostics.Debug.WriteLine(entry.Vk);
            InputEmulator.SendKey(entry.Vk);
        }

        private int GetEntryCount(int layer, int sector)
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
            int x = lx;
            int y = ly;

            int magSq = x * x + y * y;
            if (magSq < KeyboardDeadZone * KeyboardDeadZone)
            {
                _currentSector = -1;
                return -1;
            }

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

            _currentSector = sector;
            return sector;
        }





        public struct KeyEntry
        {
            public ushort Vk;
            public char Display;

            public KeyEntry(ushort vk, char display)
            {
                Vk = vk;
                Display = display;
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
    // ===== Layer 0: letters a–z =====
    {
        // 0 Up: e, t, a
        {
            new KeyEntry(VK_E, 'e'),
            new KeyEntry(VK_T, 't'),
            new KeyEntry(VK_A, 'a'),
            new KeyEntry(0,    '\0'),
        },
        // 1 Up‑Right: o, i, n
        {
            new KeyEntry(VK_O, 'o'),
            new KeyEntry(VK_I, 'i'),
            new KeyEntry(VK_N, 'n'),
            new KeyEntry(VK_Q, 'q'),
        },
        // 2 Right: s, r, h
        {
            new KeyEntry(VK_S, 's'),
            new KeyEntry(VK_R, 'r'),
            new KeyEntry(VK_H, 'h'),
            new KeyEntry(0,    '\0'),
        },
        // 3 Down‑Right: l, d, c
        {
            new KeyEntry(VK_L, 'l'),
            new KeyEntry(VK_D, 'd'),
            new KeyEntry(VK_C, 'c'),
            new KeyEntry(0,    '\0'),
        },
        // 4 Down: u, m, w
        {
            new KeyEntry(VK_U, 'u'),
            new KeyEntry(VK_M, 'm'),
            new KeyEntry(VK_W, 'w'),
            new KeyEntry(0,    '\0'),
        },
        // 5 Down‑Left: f, g, y
        {
            new KeyEntry(VK_F, 'f'),
            new KeyEntry(VK_G, 'g'),
            new KeyEntry(VK_Y, 'y'),
            new KeyEntry(0,    '\0'),
        },
        // 6 Left: p, b, v
        {
            new KeyEntry(VK_P, 'p'),
            new KeyEntry(VK_B, 'b'),
            new KeyEntry(VK_V, 'v'),
            new KeyEntry(0,    '\0'),
        },
        // 7 Up‑Left: k, j, x/z/q (rare)
        {
            new KeyEntry(VK_K, 'k'),
            new KeyEntry(VK_J, 'j'),
            new KeyEntry(VK_X, 'x'),
            new KeyEntry(VK_Z, 'z'),
        },
    },

    // ===== Layer 1: digits 0–9 =====
    {
        // 0 Up: 1, 2, 3
        {
            new KeyEntry(VK_1, '1'),
            new KeyEntry(VK_2, '2'),
            new KeyEntry(VK_3, '3'),
            new KeyEntry(0,    '\0'),
        },
        // 1 Up‑Right: 4, 5, 6
        {
            new KeyEntry(VK_4, '4'),
            new KeyEntry(VK_5, '5'),
            new KeyEntry(VK_6, '6'),
            new KeyEntry(0,    '\0'),
        },
        // 2 Right: 7, 8, 9
        {
            new KeyEntry(VK_7, '7'),
            new KeyEntry(VK_8, '8'),
            new KeyEntry(VK_9, '9'),
            new KeyEntry(0,    '\0'),
        },
        // 3 Down‑Right: 0
        {
            new KeyEntry(VK_0, '0'),
            new KeyEntry(0,    '\0'),
            new KeyEntry(0,    '\0'),
            new KeyEntry(0,    '\0'),
        },
        // 4 Down: spare
        {
            new KeyEntry(0, '\0'),
            new KeyEntry(0, '\0'),
            new KeyEntry(0, '\0'),
            new KeyEntry(0, '\0'),
        },
        // 5 Down‑Left: spare
        {
            new KeyEntry(0, '\0'),
            new KeyEntry(0, '\0'),
            new KeyEntry(0, '\0'),
            new KeyEntry(0, '\0'),
        },
        // 6 Left: spare
        {
            new KeyEntry(0, '\0'),
            new KeyEntry(0, '\0'),
            new KeyEntry(0, '\0'),
            new KeyEntry(0, '\0'),
        },
        // 7 Up‑Left: spare
        {
            new KeyEntry(0, '\0'),
            new KeyEntry(0, '\0'),
            new KeyEntry(0, '\0'),
            new KeyEntry(0, '\0'),
        },
    },

    // ===== Layer 2: punctuation / symbols =====
    {
        // 0 Up: . , ?  (most common)
        {
            new KeyEntry(VK_OEM_PERIOD, '.'),
            new KeyEntry(VK_OEM_COMMA,  ','),
            new KeyEntry(VK_OEM_2,      '?'), // Shift+'/' for ? on US
            new KeyEntry(0,             '\0'),
        },
        // 1 Up‑Right: ! @ #
        {
            new KeyEntry(VK_1, '!'),   // Shift+1
            new KeyEntry(VK_2, '@'),   // Shift+2
            new KeyEntry(VK_3, '#'),   // Shift+3
            new KeyEntry(0,    '\0'),
        },
        // 2 Right: - _ +
        {
            new KeyEntry(VK_OEM_MINUS, '-'),
            new KeyEntry(VK_OEM_MINUS, '_'), // with Shift
            new KeyEntry(VK_OEM_PLUS,  '+'),
            new KeyEntry(0,            '\0'),
        },
        // 3 Down‑Right: = & *
        {
            new KeyEntry(VK_OEM_PLUS, '='),
            new KeyEntry(VK_7,        '&'), // Shift+7
            new KeyEntry(VK_8,        '*'), // Shift+8
            new KeyEntry(0,           '\0'),
        },
        // 4 Down: ' "
        {
            new KeyEntry(VK_OEM_7, '\''),
            new KeyEntry(VK_OEM_7, '\"'),
            new KeyEntry(0,   '\0'),
            new KeyEntry(0,   '\0'),
        },
        // 5 Down‑Left: ; : :
        {
            new KeyEntry(VK_OEM_1, ';'),
            new KeyEntry(VK_OEM_1, ':'),
            new KeyEntry(0,   '\0'),
            new KeyEntry(0,   '\0'),
        },
        // 6 Left: ( ) [
        {
            new KeyEntry(VK_9,       '('), // Shift+9
            new KeyEntry(VK_0,       ')'), // Shift+0
            new KeyEntry(VK_OEM_4,   '['),
            new KeyEntry(0,          '\0'),
        },
        // 7 Up‑Left: ] \ `
        {
            new KeyEntry(VK_OEM_6, ']'),
            new KeyEntry(VK_OEM_5, '\\'),
            new KeyEntry(VK_OEM_3, '`'),
            new KeyEntry(0,        '\0'),
        },
    }
};














    }
}
