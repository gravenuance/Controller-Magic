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

    // Where synthesized input goes; returns how many events were inserted, as SendInput does.
    internal interface IInputSink
    {
        uint Send(ReadOnlySpan<INPUT> inputs);
    }

    internal sealed class SendInputSink : IInputSink
    {
        private static readonly int InputSize = Marshal.SizeOf<INPUT>();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, ref INPUT pInputs, int cbSize);

        public uint Send(ReadOnlySpan<INPUT> inputs) =>
            inputs.IsEmpty ? 0 : SendInput((uint)inputs.Length, ref MemoryMarshal.GetReference(inputs), InputSize);
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

        private readonly IInputSink _sink;
        private bool _leftIsDown;
        private bool _sendFailing;

        public InputEmulator(IInputSink sink)
        {
            _sink = sink;
        }

        public void MouseWheelVertical(int delta) => SendMouse(MOUSEEVENTF_WHEEL, unchecked((uint)delta));

        public void MouseWheelHorizontal(int delta) => SendMouse(MOUSEEVENTF_HWHEEL, unchecked((uint)delta));

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int X, int Y);

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        private const int SM_XVIRTUALSCREEN = 76;
        private const int SM_YVIRTUALSCREEN = 77;
        private const int SM_CXVIRTUALSCREEN = 78;
        private const int SM_CYVIRTUALSCREEN = 79;

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        public static void MoveMouse(int dx, int dy)
        {
            GetCursorPos(out var p);

            int targetX = p.X + dx;
            int targetY = p.Y + dy;

            int virtualLeft = GetSystemMetrics(SM_XVIRTUALSCREEN);
            int virtualTop = GetSystemMetrics(SM_YVIRTUALSCREEN);
            int virtualWidth = GetSystemMetrics(SM_CXVIRTUALSCREEN);
            int virtualHeight = GetSystemMetrics(SM_CYVIRTUALSCREEN);

            int virtualRight = virtualLeft + virtualWidth - 1;
            int virtualBottom = virtualTop + virtualHeight - 1;

            if (targetX < virtualLeft) targetX = virtualLeft;
            if (targetY < virtualTop) targetY = virtualTop;
            if (targetX > virtualRight) targetX = virtualRight;
            if (targetY > virtualBottom) targetY = virtualBottom;

            SetCursorPos(targetX, targetY);
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

        private bool SendMouse(uint flags, uint mouseData = 0)
        {
            Span<INPUT> inputs = stackalloc INPUT[1];
            inputs[0] = Mouse(flags, mouseData);
            return Send(inputs);
        }

        private static INPUT Mouse(uint flags, uint mouseData = 0)
        {
            var input = new INPUT { type = INPUT_MOUSE };
            input.U.mi.dwFlags = flags;
            input.U.mi.mouseData = mouseData;
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
            uint sent = _sink.Send(inputs);
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
