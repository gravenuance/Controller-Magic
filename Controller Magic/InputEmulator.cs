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

        public uint Send(ReadOnlySpan<INPUT> inputs) =>
            inputs.IsEmpty ? 0 : SendInput((uint)inputs.Length, ref MemoryMarshal.GetReference(inputs), InputSize);

        public Point CursorPosition => GetCursorPos(out var point) ? point : Point.Empty;

        public Rectangle VirtualScreen => new(
            GetSystemMetrics(SM_XVIRTUALSCREEN), GetSystemMetrics(SM_YVIRTUALSCREEN),
            GetSystemMetrics(SM_CXVIRTUALSCREEN), GetSystemMetrics(SM_CYVIRTUALSCREEN));

        public void SetCursorPosition(Point position) => SetCursorPos(position.X, position.Y);
    }

    internal sealed class InputEmulator
    {
        internal const uint INPUT_MOUSE = 0;
        internal const uint INPUT_KEYBOARD = 1;
        internal const uint MOUSEEVENTF_MOVE = 0x0001;
        internal const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        internal const uint MOUSEEVENTF_LEFTUP = 0x0004;
        internal const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        internal const uint MOUSEEVENTF_RIGHTUP = 0x0010;
        internal const uint MOUSEEVENTF_WHEEL = 0x0800;
        internal const uint MOUSEEVENTF_HWHEEL = 0x1000;
        internal const uint MOUSEEVENTF_VIRTUALDESK = 0x4000;
        internal const uint MOUSEEVENTF_ABSOLUTE = 0x8000;
        internal const uint KEYEVENTF_KEYUP = 0x0002;

        private const int AbsoluteScale = 65536;

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
        public void MoveMouse(int dx, int dy)
        {
            var screen = _desktop.VirtualScreen;
            if (screen.Width <= 0 || screen.Height <= 0)
                return;

            var cursor = _desktop.CursorPosition;
            var target = new Point(
                Math.Clamp(cursor.X + dx, screen.Left, screen.Right - 1),
                Math.Clamp(cursor.Y + dy, screen.Top, screen.Bottom - 1));

            var (x, y) = ToAbsolute(target, screen);
            if (!SendMouse(MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK, dx: x, dy: y))
                _desktop.SetCursorPosition(target);
        }

        // Windows maps a normalised coordinate n to pixel floor(n * size / 65536); rounding up here
        // lands on the exact pixel, so a 1 px step at slow stick speed is never rounded away.
        internal static (int X, int Y) ToAbsolute(Point pixel, Rectangle screen) =>
            (Normalise(pixel.X - screen.Left, screen.Width), Normalise(pixel.Y - screen.Top, screen.Height));

        private static int Normalise(int offset, int size) =>
            (int)((((long)offset * AbsoluteScale) + size - 1) / size);

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
