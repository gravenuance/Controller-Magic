using System.Runtime.InteropServices;

namespace ControllerMagic
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    // The desktop the synthesized input goes to.
    internal interface IDesktopInput
    {
        // Returns how many events were inserted, as SendInput does.
        uint Send(ReadOnlySpan<INPUT> inputs);

        Point CursorPosition { get; }

        Rectangle VirtualScreen { get; }

        void SetCursorPosition(Point position);

        // Resets Windows' display and sleep idle timers, as real input would.
        void KeepDisplayAwake();
    }

    internal sealed class Win32DesktopInput : IDesktopInput
    {
        private static readonly int InputSize = Marshal.SizeOf<INPUT>();

        private const int SM_XVIRTUALSCREEN = 76;
        private const int SM_YVIRTUALSCREEN = 77;
        private const int SM_CXVIRTUALSCREEN = 78;
        private const int SM_CYVIRTUALSCREEN = 79;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, ref INPUT pInputs, int cbSize);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetCursorPos(out Point lpPoint);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        private const uint ES_SYSTEM_REQUIRED = 0x00000001;
        private const uint ES_DISPLAY_REQUIRED = 0x00000002;

        [DllImport("kernel32.dll")]
        private static extern uint SetThreadExecutionState(uint esFlags);

        public uint Send(ReadOnlySpan<INPUT> inputs) =>
            inputs.IsEmpty ? 0 : SendInput((uint)inputs.Length, ref MemoryMarshal.GetReference(inputs), InputSize);

        public Point CursorPosition => GetCursorPos(out var point) ? point : Point.Empty;

        public Rectangle VirtualScreen => new(
            GetSystemMetrics(SM_XVIRTUALSCREEN), GetSystemMetrics(SM_YVIRTUALSCREEN),
            GetSystemMetrics(SM_CXVIRTUALSCREEN), GetSystemMetrics(SM_CYVIRTUALSCREEN));

        public void SetCursorPosition(Point position) => SetCursorPos(position.X, position.Y);

        private bool _keepAwakeFailureLogged;

        // Without ES_CONTINUOUS this is a one-off reset, so nothing is left held when moves stop.
        public void KeepDisplayAwake()
        {
            if (SetThreadExecutionState(ES_DISPLAY_REQUIRED | ES_SYSTEM_REQUIRED) != 0 || _keepAwakeFailureLogged)
                return;

            _keepAwakeFailureLogged = true;
            AppLog.Default.Warning("InputEmulator: SetThreadExecutionState failed; the display may dim while the stick moves the cursor");
        }
    }

    internal sealed class InputEmulator
    {
        internal const uint INPUT_MOUSE = 0;
        internal const uint INPUT_KEYBOARD = 1;
        internal const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        internal const uint MOUSEEVENTF_LEFTUP = 0x0004;
        internal const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        internal const uint MOUSEEVENTF_RIGHTUP = 0x0010;
        internal const uint MOUSEEVENTF_WHEEL = 0x0800;
        internal const uint MOUSEEVENTF_HWHEEL = 0x1000;
        internal const uint KEYEVENTF_KEYUP = 0x0002;

        private readonly IDesktopInput _desktop;
        private bool _leftIsDown;
        private bool _sendFailing;

        public InputEmulator(IDesktopInput desktop)
        {
            _desktop = desktop;
        }

        public void MouseWheelVertical(int delta) => SendMouse(MOUSEEVENTF_WHEEL, unchecked((uint)delta));

        public void MouseWheelHorizontal(int delta) => SendMouse(MOUSEEVENTF_HWHEEL, unchecked((uint)delta));

        // Sent as input, unlike SetCursorPos, so it keeps the display awake and resets idle timers.
        // Absolute, because relative moves go through "Enhance pointer precision" acceleration on
        // top of the stick's own curve. SetCursorPos stays as the fallback where UIPI drops the
        // input (an elevated window in front), so the cursor still moves there as before.
        // SetCursorPos, not an absolute SendInput: Windows maps absolute coordinates back to pixels
        // 1 px short most of the time, and each move starts where the last one landed.
        public void MoveMouse(int dx, int dy)
        {
            var screen = _desktop.VirtualScreen;
            if (screen.Width <= 0 || screen.Height <= 0)
                return;

            var cursor = _desktop.CursorPosition;
            _desktop.SetCursorPosition(new Point(
                Math.Clamp(cursor.X + dx, screen.Left, screen.Right - 1),
                Math.Clamp(cursor.Y + dy, screen.Top, screen.Bottom - 1)));
            _desktop.KeepDisplayAwake();
        }

        // Committed only once Windows accepts it: UIPI drops input aimed at an elevated window, and
        // the caller re-asserts the wanted state every tick, so a dropped press or release is retried.
        public void SetLeftButtonState(bool pressed)
        {
            if (pressed == _leftIsDown) return;
            if (SendMouse(pressed ? MOUSEEVENTF_LEFTDOWN : MOUSEEVENTF_LEFTUP))
                _leftIsDown = pressed;
        }

        public void LeftClick() => Click(MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_LEFTUP);

        public void RightClick() => Click(MOUSEEVENTF_RIGHTDOWN, MOUSEEVENTF_RIGHTUP);

        public void SendKey(ushort vk)
        {
            Span<INPUT> inputs = stackalloc INPUT[2];
            inputs[0] = Key(vk, up: false);
            inputs[1] = Key(vk, up: true);
            Send(inputs);
        }

        // One batch, so nothing typed or clicked in between can land with the modifier held.
        public void SendKeyWithModifier(ushort modifierVk, ushort vk)
        {
            Span<INPUT> inputs = stackalloc INPUT[4];
            inputs[0] = Key(modifierVk, up: false);
            inputs[1] = Key(vk, up: false);
            inputs[2] = Key(vk, up: true);
            inputs[3] = Key(modifierVk, up: true);
            Send(inputs);
        }

        public void LeftClickWithModifier(ushort modifierVk)
        {
            Span<INPUT> inputs = stackalloc INPUT[4];
            inputs[0] = Key(modifierVk, up: false);
            inputs[1] = Mouse(MOUSEEVENTF_LEFTDOWN);
            inputs[2] = Mouse(MOUSEEVENTF_LEFTUP);
            inputs[3] = Key(modifierVk, up: true);
            Send(inputs);
        }

        private void Click(uint downFlag, uint upFlag)
        {
            Span<INPUT> inputs = stackalloc INPUT[2];
            inputs[0] = Mouse(downFlag);
            inputs[1] = Mouse(upFlag);
            Send(inputs);
        }

        private bool SendMouse(uint flags, uint mouseData = 0, int dx = 0, int dy = 0)
        {
            Span<INPUT> inputs = stackalloc INPUT[1];
            inputs[0] = Mouse(flags, mouseData, dx, dy);
            return Send(inputs);
        }

        private static INPUT Mouse(uint flags, uint mouseData = 0, int dx = 0, int dy = 0)
        {
            var input = new INPUT { type = INPUT_MOUSE };
            input.U.mi.dwFlags = flags;
            input.U.mi.mouseData = mouseData;
            input.U.mi.dx = dx;
            input.U.mi.dy = dy;
            return input;
        }

        private static INPUT Key(ushort vk, bool up)
        {
            var input = new INPUT { type = INPUT_KEYBOARD };
            input.U.ki.wVk = vk;
            input.U.ki.dwFlags = up ? KEYEVENTF_KEYUP : 0;
            return input;
        }

        // Logged once per run of failures: a blocked foreground window rejects every tick's input.
        private bool Send(ReadOnlySpan<INPUT> inputs)
        {
            uint sent = _desktop.Send(inputs);
            if (sent == inputs.Length)
            {
                _sendFailing = false;
                return true;
            }

            if (!_sendFailing)
            {
                AppLog.Default.Warning(
                    $"InputEmulator: SendInput inserted {sent} of {inputs.Length} events (Win32 error {Marshal.GetLastPInvokeError()}); the foreground window may be elevated");
                _sendFailing = true;
            }

            return false;
        }
    }
}
