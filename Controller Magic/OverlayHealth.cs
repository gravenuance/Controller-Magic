namespace ControllerMagic;

[Flags]
internal enum OverlayProblem
{
    None = 0,
    NotPainted = 1 << 0,
    Hidden = 1 << 1,
    Cloaked = 1 << 2,
    NotTopMost = 1 << 3,
    OffScreen = 1 << 4,
    Covered = 1 << 5,
    TimerStalled = 1 << 6,
    Transparent = 1 << 7,
}

// Everything that decides whether the overlay's pixels can reach the screen - the app can't read
// back what DWM actually composed, so these are the proxies.
internal readonly record struct OverlaySnapshot(
    bool Painted,
    bool Visible,
    bool Cloaked,
    bool TopMost,
    bool OnScreen,
    bool Covered,
    bool TimerTicking,
    double Opacity);

internal static class OverlayHealth
{
    public static OverlayProblem Evaluate(OverlaySnapshot s)
    {
        var problems = OverlayProblem.None;
        if (!s.Painted) problems |= OverlayProblem.NotPainted;
        if (!s.Visible) problems |= OverlayProblem.Hidden;
        if (s.Cloaked) problems |= OverlayProblem.Cloaked;
        if (!s.TopMost) problems |= OverlayProblem.NotTopMost;
        if (!s.OnScreen) problems |= OverlayProblem.OffScreen;
        if (s.Covered) problems |= OverlayProblem.Covered;
        if (!s.TimerTicking) problems |= OverlayProblem.TimerStalled;
        if (s.Opacity < 1.0) problems |= OverlayProblem.Transparent;
        return problems;
    }
}
