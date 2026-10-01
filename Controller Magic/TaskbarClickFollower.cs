using System.ComponentModel;
using System.Runtime.InteropServices;

namespace ControllerMagic;

internal enum WindowReadiness
{
    // Shell surfaces, tool windows, owned dialogs and our own windows are never moved.
    NotAnAppWindow,

    // An app window still minimized, hidden or cloaked; it's looked at again next tick.
    NotYetShown,
    Ready,
}

internal interface IDesktopWindows
{
    Point CursorPosition { get; }

    nint ForegroundWindow { get; }

    // The taskbar on any monitor, or its thumbnail previews.
    bool IsTaskbarAt(Point point);

    nint MonitorAt(Point point);

    nint MonitorOf(nint window);

    HashSet<nint> TopLevelWindows();

    WindowReadiness Readiness(nint window);

    bool IsMinimized(nint window);

    // Returns at once and logs a refusal; a move waits on the window's own thread, which a launching app keeps busy.
    void MoveToMonitor(nint window, nint monitor);
}

// Windows lets each app pick its own monitor, so a taskbar click on one screen can open or bring up
// a window on another; this moves the windows a controller taskbar click brings up to the cursor's.
internal sealed class TaskbarClickFollower(TimeProvider clock, IDesktopWindows desktop)
{
    // Long enough for a cold launch with a splash screen.
    internal static readonly TimeSpan LaunchWindow = TimeSpan.FromSeconds(10);

    // Clicking an open app's button brings it up at once; later switches are the user's own.
    internal static readonly TimeSpan ActivationWindow = TimeSpan.FromSeconds(1.5);

    private sealed class Armed
    {
        public required long Since { get; init; }
        public required nint Monitor { get; init; }
        public required HashSet<nint> ExistingWindows { get; init; }
        public required nint PriorAppWindow { get; init; }
        public nint LastForeground { get; set; }
    }

    private Armed? _armed;
    private nint _lastSeenForeground;
    private nint _lastAppForeground;

    public bool IsArmed => _armed != null;

    // A completed controller left click: on the taskbar it starts watching, anywhere else it stops.
    public void OnControllerClick()
    {
        var cursor = desktop.CursorPosition;
        if (!desktop.IsTaskbarAt(cursor))
        {
            _armed = null;
            return;
        }

        _armed = new Armed
        {
            Since = clock.GetTimestamp(),
            Monitor = desktop.MonitorAt(cursor),
            ExistingWindows = desktop.TopLevelWindows(),
            PriorAppWindow = _lastAppForeground,
        };
    }

    public void Tick()
    {
        nint foreground = desktop.ForegroundWindow;
        if (_armed == null)
        {
            TrackAppForeground(foreground);
            return;
        }

        var elapsed = clock.GetElapsedTime(_armed.Since);
        if (elapsed > LaunchWindow)
        {
            _armed = null;
            return;
        }

        if (foreground == 0 || foreground == _armed.LastForeground)
            return;

        var readiness = desktop.Readiness(foreground);
        switch (readiness)
        {
            case WindowReadiness.NotAnAppWindow:
                _armed.LastForeground = foreground;
                return;
            case WindowReadiness.NotYetShown:
                return;
            case WindowReadiness.Ready:
                break;
            default:
                throw new InvalidOperationException($"Unhandled window readiness {readiness}");
        }

        _armed.LastForeground = foreground;
        _lastAppForeground = foreground;

        if (_armed.ExistingWindows.Contains(foreground))
        {
            // Only the first window brought up by the click counts, and none if the click minimized the active app.
            var armed = _armed;
            _armed = null;
            if (elapsed > ActivationWindow || (armed.PriorAppWindow != 0 && desktop.IsMinimized(armed.PriorAppWindow)))
                return;
            Move(foreground, armed.Monitor);
            return;
        }

        // A launch stays armed: a splash screen comes up before the main window.
        Move(foreground, _armed.Monitor);
    }

    // Remembers the active app, so a click on its own button (which minimizes it) can be told apart.
    private void TrackAppForeground(nint foreground)
    {
        if (foreground == _lastSeenForeground)
            return;
        _lastSeenForeground = foreground;
        if (foreground != 0 && desktop.Readiness(foreground) == WindowReadiness.Ready)
            _lastAppForeground = foreground;
    }

    private void Move(nint window, nint monitor)
    {
        if (desktop.MonitorOf(window) != monitor)
            desktop.MoveToMonitor(window, monitor);
    }

    // Keeps the window's centre at the same relative spot in the target work area, shrunk to fit inside it.
    internal static Rectangle Relocate(Rectangle window, Rectangle fromWork, Rectangle toWork)
    {
        int width = Math.Min(window.Width, toWork.Width);
        int height = Math.Min(window.Height, toWork.Height);

        double centreX = fromWork.Width > 0 ? (window.X + window.Width / 2.0 - fromWork.X) / fromWork.Width : 0.5;
        double centreY = fromWork.Height > 0 ? (window.Y + window.Height / 2.0 - fromWork.Y) / fromWork.Height : 0.5;

        int x = (int)Math.Round(toWork.X + centreX * toWork.Width - width / 2.0);
        int y = (int)Math.Round(toWork.Y + centreY * toWork.Height - height / 2.0);

        x = Math.Clamp(x, toWork.Left, toWork.Right - width);
        y = Math.Clamp(y, toWork.Top, toWork.Bottom - height);
        return new Rectangle(x, y, width, height);
    }
}

internal sealed class Win32DesktopWindows : IDesktopWindows
{
    private static readonly HashSet<string> TaskbarClasses = new(StringComparer.Ordinal)
    {
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd",
        "TaskListThumbnailWnd",
        "XamlExplorerHostIslandWindow",
    };

    // Start, search, flyouts, the desktop and task switchers already open where the taskbar was clicked.
    private static readonly HashSet<string> ShellClasses = new(StringComparer.Ordinal)
    {
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd",
        "TaskListThumbnailWnd",
        "XamlExplorerHostIslandWindow",
        "Windows.UI.Core.CoreWindow",
        "NotifyIconOverflowWindow",
        "TopLevelWindowForOverflowXamlIsland",
        "MultitaskingViewFrame",
        "ForegroundStaging",
        "Progman",
        "WorkerW",
    };

    private const uint GA_ROOT = 2;
    private const uint GW_OWNER = 4;
    private const int GWL_STYLE = -16;
    private const int GWL_EXSTYLE = -20;
    private const long WS_CHILD = 0x40000000;
    private const long WS_EX_TOOLWINDOW = 0x00000080;
    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private const int DWMWA_CLOAKED = 14;
    private const int SW_SHOWMAXIMIZED = 3;
    private const int SW_SHOWNOACTIVATE = 4;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public readonly Rectangle ToRectangle() => Rectangle.FromLTRB(Left, Top, Right, Bottom);

        public static RECT From(Rectangle r) => new() { Left = r.Left, Top = r.Top, Right = r.Right, Bottom = r.Bottom };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOWPLACEMENT
    {
        public int length;
        public int flags;
        public int showCmd;
        public Point ptMinPosition;
        public Point ptMaxPosition;
        public RECT rcNormalPosition;
    }

    private delegate bool EnumWindowsProc(nint hWnd, nint lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out Point lpPoint);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern nint WindowFromPoint(Point point);

    [DllImport("user32.dll")]
    private static extern nint GetAncestor(nint hwnd, uint gaFlags);

    [DllImport("user32.dll")]
    private static extern nint GetWindow(nint hWnd, uint uCmd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint hWnd, char[] lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern nint GetWindowLongPtr(nint hWnd, int nIndex);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsZoomed(nint hWnd);

    [DllImport("user32.dll")]
    private static extern int GetWindowThreadProcessId(nint hWnd, out int lpdwProcessId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, nint lParam);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromPoint(Point pt, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint hwnd, uint dwFlags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowPlacement(nint hWnd, ref WINDOWPLACEMENT lpwndpl);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPlacement(nint hWnd, ref WINDOWPLACEMENT lpwndpl);

    // Returns whether the window was visible before, not success.
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint hWnd, int nCmdShow);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint hWnd, out RECT lpRect);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(nint hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

    // Reused on the poll thread only; class names are at most 256 characters.
    private readonly char[] _className = new char[257];

    public Point CursorPosition => GetCursorPos(out var point) ? point : Point.Empty;

    public nint ForegroundWindow => GetForegroundWindow();

    public bool IsTaskbarAt(Point point)
    {
        nint root = GetAncestor(WindowFromPoint(point), GA_ROOT);
        return root != 0 && TaskbarClasses.Contains(ClassName(root));
    }

    public nint MonitorAt(Point point) => MonitorFromPoint(point, MONITOR_DEFAULTTONEAREST);

    public nint MonitorOf(nint window) => MonitorFromWindow(window, MONITOR_DEFAULTTONEAREST);

    public HashSet<nint> TopLevelWindows()
    {
        var windows = new HashSet<nint>();
        EnumWindows((hWnd, _) => windows.Add(hWnd) || true, 0);
        return windows;
    }

    public WindowReadiness Readiness(nint window)
    {
        if (GetWindow(window, GW_OWNER) != 0
            || (GetWindowLongPtr(window, GWL_STYLE) & WS_CHILD) != 0
            || (GetWindowLongPtr(window, GWL_EXSTYLE) & WS_EX_TOOLWINDOW) != 0
            || ShellClasses.Contains(ClassName(window)))
            return WindowReadiness.NotAnAppWindow;

        _ = GetWindowThreadProcessId(window, out int pid);
        if (pid == Environment.ProcessId)
            return WindowReadiness.NotAnAppWindow;

        bool cloaked = DwmGetWindowAttribute(window, DWMWA_CLOAKED, out int cloak, sizeof(int)) == 0 && cloak != 0;
        return !IsWindowVisible(window) || IsIconic(window) || cloaked ? WindowReadiness.NotYetShown : WindowReadiness.Ready;
    }

    public bool IsMinimized(nint window) => IsIconic(window);

    public void MoveToMonitor(nint window, nint monitor) => _ = Task.Run(() =>
    {
        try
        {
            MoveNow(window, monitor);
        }
        catch (Win32Exception ex)
        {
            AppLog.Default.Warning($"TaskbarClickFollower: couldn't move window 0x{window:X} to the cursor's monitor (Win32 error {ex.NativeErrorCode}); it may be elevated", ex);
        }
    });

    private void MoveNow(nint window, nint monitor)
    {
        var from = WorkArea(MonitorOf(window));
        var to = WorkArea(monitor);

        if (IsZoomed(window))
        {
            // SetWindowPlacement alone only changes a maximized window's restore rect, so it's
            // restored onto the target monitor and maximized again there.
            var placement = new WINDOWPLACEMENT { length = Marshal.SizeOf<WINDOWPLACEMENT>() };
            if (!GetWindowPlacement(window, ref placement))
                throw new Win32Exception();

            // Restore rects are in workspace coordinates, offset from the screen's by the primary taskbar.
            var offset = WorkspaceOffset();
            var normal = placement.rcNormalPosition.ToRectangle();
            normal.Offset(offset);
            var moved = TaskbarClickFollower.Relocate(normal, from, to);
            moved.Offset(-offset.X, -offset.Y);
            placement.rcNormalPosition = RECT.From(moved);
            placement.showCmd = SW_SHOWNOACTIVATE;
            if (!SetWindowPlacement(window, ref placement))
                throw new Win32Exception();
            _ = ShowWindow(window, SW_SHOWMAXIMIZED);
            return;
        }

        if (!GetWindowRect(window, out var rect))
            throw new Win32Exception();
        var target = TaskbarClickFollower.Relocate(rect.ToRectangle(), from, to);
        if (!SetWindowPos(window, 0, target.X, target.Y, target.Width, target.Height, SWP_NOZORDER | SWP_NOACTIVATE))
            throw new Win32Exception();
    }

    private static Rectangle WorkArea(nint monitor)
    {
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(monitor, ref info))
            throw new Win32Exception();
        return info.rcWork.ToRectangle();
    }

    private static Point WorkspaceOffset()
    {
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(MonitorFromPoint(Point.Empty, MONITOR_DEFAULTTONEAREST), ref info))
            throw new Win32Exception();
        return new Point(info.rcWork.Left - info.rcMonitor.Left, info.rcWork.Top - info.rcMonitor.Top);
    }

    private string ClassName(nint window)
    {
        int length = GetClassName(window, _className, _className.Length);
        return length > 0 ? new string(_className, 0, length) : string.Empty;
    }
}
