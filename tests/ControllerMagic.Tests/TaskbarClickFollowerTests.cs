using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace ControllerMagic.Tests;

public sealed class TaskbarClickFollowerTests
{
    private const nint CursorScreen = 1;
    private const nint OtherScreen = 2;
    private const nint OpenApp = 100;
    private const nint OtherOpenApp = 101;
    private const nint NewApp = 200;
    private const nint Splash = 201;
    private const nint Flyout = 300;

    private static readonly Point OnTaskbar = new(500, 1060);
    private static readonly Point OnDesktop = new(500, 500);

    private readonly FakeTimeProvider _clock = new();
    private readonly FakeDesktopWindows _desktop = new();
    private readonly TaskbarClickFollower _follower;

    public TaskbarClickFollowerTests()
    {
        _follower = new TaskbarClickFollower(_clock, _desktop);
        _desktop.AddWindow(OpenApp, OtherScreen);
        _desktop.AddWindow(OtherOpenApp, OtherScreen);
        _desktop.AddWindow(Flyout, CursorScreen, WindowReadiness.NotAnAppWindow);
        _desktop.TaskbarAt = OnTaskbar;
        _desktop.ScreenUnderCursor = CursorScreen;
    }

    private void ClickAt(Point point)
    {
        _desktop.CursorPosition = point;
        _follower.OnControllerClick();
    }

    private void Bring(nint window, TimeSpan after)
    {
        _clock.Advance(after);
        _desktop.ForegroundWindow = window;
        _follower.Tick();
    }

    private void Launch(nint window, nint screen, TimeSpan after)
    {
        _desktop.AddWindow(window, screen);
        Bring(window, after);
    }

    [Fact]
    public void Tick_AppLaunchedFromTheTaskbarOnAnotherScreen_MovesItToTheCursorsScreen()
    {
        ClickAt(OnTaskbar);
        Launch(NewApp, OtherScreen, TimeSpan.FromSeconds(3));

        Assert.Equal([(NewApp, CursorScreen)], _desktop.Moves);
    }

    [Fact]
    public void Tick_AppLaunchedOnTheCursorsScreen_LeavesItWhereItIs()
    {
        ClickAt(OnTaskbar);
        Launch(NewApp, CursorScreen, TimeSpan.FromSeconds(3));

        Assert.Empty(_desktop.Moves);
    }

    [Fact]
    public void Tick_SplashScreenThenMainWindow_MovesBoth()
    {
        ClickAt(OnTaskbar);
        Launch(Splash, OtherScreen, TimeSpan.FromSeconds(1));
        Launch(NewApp, OtherScreen, TimeSpan.FromSeconds(4));

        Assert.Equal([(Splash, CursorScreen), (NewApp, CursorScreen)], _desktop.Moves);
    }

    [Fact]
    public void Tick_AppAppearingAfterTheLaunchWindow_LeavesItWhereItIs()
    {
        ClickAt(OnTaskbar);
        Launch(NewApp, OtherScreen, TimeSpan.FromSeconds(11));

        Assert.Empty(_desktop.Moves);
    }

    [Fact]
    public void Tick_ClickOffTheTaskbar_MovesNothing()
    {
        ClickAt(OnDesktop);
        Launch(NewApp, OtherScreen, TimeSpan.FromSeconds(1));

        Assert.Empty(_desktop.Moves);
    }

    [Fact]
    public void Tick_ClickElsewhereAfterTheTaskbar_StopsWatching()
    {
        ClickAt(OnTaskbar);
        ClickAt(OnDesktop);
        Launch(NewApp, OtherScreen, TimeSpan.FromSeconds(1));

        Assert.Empty(_desktop.Moves);
        Assert.False(_follower.IsArmed);
    }

    [Fact]
    public void Tick_OpenAppBroughtUpByTheClick_MovesItToTheCursorsScreen()
    {
        ClickAt(OnTaskbar);
        Bring(OpenApp, TimeSpan.FromMilliseconds(50));

        Assert.Equal([(OpenApp, CursorScreen)], _desktop.Moves);
    }

    [Fact]
    public void Tick_SwitchingAppsAfterTheClickBroughtOneUp_LeavesTheNextOneAlone()
    {
        ClickAt(OnTaskbar);
        Bring(OpenApp, TimeSpan.FromMilliseconds(50));
        Bring(OtherOpenApp, TimeSpan.FromMilliseconds(500));

        Assert.Equal([(OpenApp, CursorScreen)], _desktop.Moves);
        Assert.False(_follower.IsArmed);
    }

    [Fact]
    public void Tick_OpenAppComingUpLongAfterTheClick_LeavesItWhereItIs()
    {
        ClickAt(OnTaskbar);
        Bring(OpenApp, TimeSpan.FromSeconds(2));

        Assert.Empty(_desktop.Moves);
    }

    [Fact]
    public void Tick_ClickMinimizedTheActiveApp_LeavesTheWindowWindowsActivatesNextAlone()
    {
        Bring(OpenApp, TimeSpan.Zero);
        ClickAt(OnTaskbar);
        _desktop.Minimized.Add(OpenApp);
        Bring(OtherOpenApp, TimeSpan.FromMilliseconds(50));

        Assert.Empty(_desktop.Moves);
    }

    [Fact]
    public void Tick_WindowStillRestoring_MovesItOnceShown()
    {
        _desktop.SetReadiness(OpenApp, WindowReadiness.NotYetShown);
        ClickAt(OnTaskbar);
        Bring(OpenApp, TimeSpan.FromMilliseconds(20));
        Assert.Empty(_desktop.Moves);

        _desktop.SetReadiness(OpenApp, WindowReadiness.Ready);
        Bring(OpenApp, TimeSpan.FromMilliseconds(20));

        Assert.Equal([(OpenApp, CursorScreen)], _desktop.Moves);
    }

    [Fact]
    public void Tick_ShellFlyoutFirst_StillMovesTheAppThatFollows()
    {
        ClickAt(OnTaskbar);
        Bring(Flyout, TimeSpan.FromMilliseconds(20));
        Bring(OpenApp, TimeSpan.FromMilliseconds(20));

        Assert.Equal([(OpenApp, CursorScreen)], _desktop.Moves);
    }

    [Fact]
    public void Relocate_CentredWindowBetweenEqualScreens_StaysCentred()
    {
        var moved = TaskbarClickFollower.Relocate(
            new Rectangle(2360, 216, 1000, 600), new Rectangle(1920, 0, 1920, 1032), new Rectangle(0, 0, 1920, 1032));

        Assert.Equal(new Rectangle(440, 216, 1000, 600), moved);
    }

    [Fact]
    public void Relocate_ToASmallerScreen_KeepsTheRelativeSpot()
    {
        var moved = TaskbarClickFollower.Relocate(
            new Rectangle(100, 100, 400, 200), new Rectangle(0, 0, 2000, 1000), new Rectangle(3000, 0, 1000, 500));

        Assert.Equal(new Rectangle(3000, 0, 400, 200), moved);
    }

    [Fact]
    public void Relocate_WindowLargerThanTheTarget_ShrinksToFitInside()
    {
        var moved = TaskbarClickFollower.Relocate(
            new Rectangle(0, 0, 2560, 1400), new Rectangle(0, 0, 2560, 1400), new Rectangle(2560, 100, 1920, 1032));

        Assert.Equal(new Rectangle(2560, 100, 1920, 1032), moved);
    }

    [Fact]
    public void Relocate_WindowNearTheEdge_ClampsIntoTheTarget()
    {
        var moved = TaskbarClickFollower.Relocate(
            new Rectangle(1700, 900, 400, 300), new Rectangle(0, 0, 1920, 1032), new Rectangle(1920, 0, 1920, 1032));

        Assert.Equal(new Rectangle(3440, 732, 400, 300), moved);
    }

    private sealed class FakeDesktopWindows : IDesktopWindows
    {
        private readonly Dictionary<nint, nint> _screens = [];
        private readonly Dictionary<nint, WindowReadiness> _readiness = [];

        public Point CursorPosition { get; set; }
        public nint ForegroundWindow { get; set; }
        public Point TaskbarAt { get; set; }
        public nint ScreenUnderCursor { get; set; }
        public HashSet<nint> Minimized { get; } = [];
        public List<(nint Window, nint Screen)> Moves { get; } = [];

        public void AddWindow(nint window, nint screen, WindowReadiness readiness = WindowReadiness.Ready)
        {
            _screens[window] = screen;
            _readiness[window] = readiness;
        }

        public void SetReadiness(nint window, WindowReadiness readiness) => _readiness[window] = readiness;

        public bool IsTaskbarAt(Point point) => point == TaskbarAt;

        public nint MonitorAt(Point point) => ScreenUnderCursor;

        public nint MonitorOf(nint window) => _screens[window];

        public HashSet<nint> TopLevelWindows() => [.. _screens.Keys];

        public WindowReadiness Readiness(nint window) => _readiness[window];

        public bool IsMinimized(nint window) => Minimized.Contains(window);

        public void MoveToMonitor(nint window, nint monitor)
        {
            _screens[window] = monitor;
            Moves.Add((window, monitor));
        }
    }
}
