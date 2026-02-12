using System.Runtime.InteropServices;

namespace ControllerMagic
{
    internal static class InputEmulator
    {
        [StructLayout(LayoutKind.Sequential)]
        struct INPUT
        {
            public uint type;
            public InputUnion U;
        }

        [StructLayout(LayoutKind.Explicit)]
        struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [Flags]
        private enum MouseEventFlags : uint
        {
            MOUSEEVENTF_WHEEL = 0x0800,
            MOUSEEVENTF_HWHEEL = 0x01000
        }

        [DllImport("user32.dll")]
        private static extern void mouse_event(
        uint dwFlags,
        uint dx,
        uint dy,
        uint dwData,
        UIntPtr dwExtraInfo);

        public static void MouseWheelVertical(int delta)
        {
            // delta: +120 up, -120 down (standard Windows wheel step)
            mouse_event((uint)MouseEventFlags.MOUSEEVENTF_WHEEL, 0, 0, (uint)delta, UIntPtr.Zero);
        }

        public static void MouseWheelHorizontal(int delta)
        {
            mouse_event((uint)MouseEventFlags.MOUSEEVENTF_HWHEEL, 0, 0, (uint)delta, UIntPtr.Zero);
        }

        [StructLayout(LayoutKind.Sequential)]
        struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        const uint INPUT_MOUSE = 0;
        const uint INPUT_KEYBOARD = 1;
        //const uint MOUSEEVENTF_MOVE = 0x0001;
        const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        const uint MOUSEEVENTF_LEFTUP = 0x0004;
        const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        const uint MOUSEEVENTF_RIGHTUP = 0x0010;
        //const uint MOUSEEVENTF_ABSOLUTE = 0x8000;   // add this
        const uint KEYEVENTF_KEYUP = 0x0002;

        [DllImport("user32.dll", SetLastError = true)]
        static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int X, int Y); // [web:93]

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex); // [web:103]

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

        static bool _leftIsDown;
        public static void SetLeftButtonState(bool pressed)
        {
            if (pressed == _leftIsDown) return;
            _leftIsDown = pressed;
            var inputs = new INPUT[1];
            inputs[0].type = INPUT_MOUSE;
            inputs[0].U.mi.dwFlags = pressed ? MOUSEEVENTF_LEFTDOWN : MOUSEEVENTF_LEFTUP;
            _ = SendInput(1, inputs, Marshal.SizeOf<INPUT>());
        }
        public static void SetRightButtonState(bool pressed)
        {
            if (pressed == _leftIsDown) return;
            _leftIsDown = pressed;
            var inputs = new INPUT[1];
            inputs[0].type = INPUT_MOUSE;
            inputs[0].U.mi.dwFlags = pressed ? MOUSEEVENTF_RIGHTDOWN : MOUSEEVENTF_RIGHTUP;
            _ = SendInput(1, inputs, Marshal.SizeOf<INPUT>());
        }
        public static void LeftClick()
        {
            var inputs = new INPUT[2];

            inputs[0].type = INPUT_MOUSE;
            inputs[0].U.mi.dwFlags = MOUSEEVENTF_LEFTDOWN;

            inputs[1].type = INPUT_MOUSE;
            inputs[1].U.mi.dwFlags = MOUSEEVENTF_LEFTUP;

            _ = SendInput(2, inputs, Marshal.SizeOf<INPUT>());
        }
        public static void RightClick()
        {
            var inputs = new INPUT[2];

            inputs[0].type = INPUT_MOUSE;
            inputs[0].U.mi.dwFlags = MOUSEEVENTF_RIGHTDOWN;

            inputs[1].type = INPUT_MOUSE;
            inputs[1].U.mi.dwFlags = MOUSEEVENTF_RIGHTUP;

            _ = SendInput(2, inputs, Marshal.SizeOf<INPUT>());
        }


        public static void SendKey(ushort vk)
        {
            System.Diagnostics.Debug.WriteLine(vk);
            var inputs = new INPUT[2];

            inputs[0].type = INPUT_KEYBOARD;
            inputs[0].U.ki.wVk = vk; // key down

            inputs[1].type = INPUT_KEYBOARD;
            inputs[1].U.ki.wVk = vk;
            inputs[1].U.ki.dwFlags = KEYEVENTF_KEYUP; // key up

            _ = SendInput(2, inputs, Marshal.SizeOf<INPUT>());
        }

        public static void SendKey(ushort vk, bool pressed)
        {
            System.Diagnostics.Debug.WriteLine(vk);
            var inputs = new INPUT[1];

            inputs[0].type = INPUT_KEYBOARD;
            inputs[0].U.ki.wVk = vk; // key down
            if (!pressed)
                inputs[0].U.ki.dwFlags = KEYEVENTF_KEYUP;

            _ = SendInput(1, inputs, Marshal.SizeOf<INPUT>());
        }
    }
}
