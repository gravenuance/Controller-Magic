using System.Runtime.InteropServices;

namespace ControllerMagic;

// Win32/DWM reads behind OverlaySnapshot, plus the one z-order repair they can call for.
internal static class OverlayWindowProbe
{
    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TOPMOST = 0x00000008;
    private const long WS_EX_TRANSPARENT = 0x00000020;
    private const int DWMWA_CLOAKED = 14;
    private const uint GW_HWNDPREV = 3;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private static readonly IntPtr HWND_TOPMOST = new(-1);

    // Bounds the z-order walk; a desktop rarely has more than a few dozen top-level windows above.
    private const int MaxWindowsAbove = 256;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public readonly Rectangle ToRectangle() => Rectangle.FromLTRB(Left, Top, Right, Bottom);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

    public static OverlaySnapshot Capture(Form form, bool painted, bool timerTicking)
    {
        IntPtr hwnd = form.Handle;
        var bounds = GetBounds(hwnd);

        return new OverlaySnapshot(
            Painted: painted,
            Visible: IsWindowVisible(hwnd),
            Cloaked: IsCloaked(hwnd),
            TopMost: (GetExStyle(hwnd) & WS_EX_TOPMOST) != 0,
            OnScreen: Screen.AllScreens.Any(s => s.Bounds.IntersectsWith(bounds)),
            Covered: IsCoveredByAnotherWindow(hwnd, bounds),
            TimerTicking: timerTicking,
            Opacity: form.Opacity);
    }

    public static void BringToTopmost(IntPtr hwnd)
    {
        if (!SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW))
            AppLog.Default.Warning($"OverlayWindowProbe: SetWindowPos failed (Win32 error {Marshal.GetLastPInvokeError()})");
    }

    // Click-through (WS_EX_TRANSPARENT) windows above are almost always see-through overlays too,
    // so only solid, visible, uncloaked windows from other processes count as covering.
    private static bool IsCoveredByAnotherWindow(IntPtr hwnd, Rectangle bounds)
    {
        uint ownProcessId = (uint)Environment.ProcessId;
        IntPtr above = GetWindow(hwnd, GW_HWNDPREV);

        for (int i = 0; above != IntPtr.Zero && i < MaxWindowsAbove; i++, above = GetWindow(above, GW_HWNDPREV))
        {
            if (!IsWindowVisible(above) || IsCloaked(above) || (GetExStyle(above) & WS_EX_TRANSPARENT) != 0)
                continue;

            _ = GetWindowThreadProcessId(above, out uint processId);
            if (processId != ownProcessId && GetBounds(above).IntersectsWith(bounds))
                return true;
        }

        return false;
    }

    private static long GetExStyle(IntPtr hwnd) => GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();

    private static bool IsCloaked(IntPtr hwnd) =>
        DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0;

    private static Rectangle GetBounds(IntPtr hwnd) =>
        GetWindowRect(hwnd, out RECT rect) ? rect.ToRectangle() : Rectangle.Empty;
}
