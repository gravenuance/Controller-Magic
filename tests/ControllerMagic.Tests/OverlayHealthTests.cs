using ControllerMagic;
using Xunit;

namespace ControllerMagic.Tests;

public class OverlayHealthTests
{
    private static readonly OverlaySnapshot Healthy = new(
        Painted: true, Visible: true, Cloaked: false, TopMost: true,
        OnScreen: true, Covered: false, TimerTicking: true, Opacity: 1.0);

    [Fact]
    public void Evaluate_HealthySnapshot_NoProblems()
    {
        Assert.Equal(OverlayProblem.None, OverlayHealth.Evaluate(Healthy));
    }

    [Fact]
    public void Evaluate_EachFaultIsReportedOnItsOwn()
    {
        Assert.Equal(OverlayProblem.NotPainted, OverlayHealth.Evaluate(Healthy with { Painted = false }));
        Assert.Equal(OverlayProblem.Hidden, OverlayHealth.Evaluate(Healthy with { Visible = false }));
        Assert.Equal(OverlayProblem.Cloaked, OverlayHealth.Evaluate(Healthy with { Cloaked = true }));
        Assert.Equal(OverlayProblem.NotTopMost, OverlayHealth.Evaluate(Healthy with { TopMost = false }));
        Assert.Equal(OverlayProblem.OffScreen, OverlayHealth.Evaluate(Healthy with { OnScreen = false }));
        Assert.Equal(OverlayProblem.Covered, OverlayHealth.Evaluate(Healthy with { Covered = true }));
        Assert.Equal(OverlayProblem.TimerStalled, OverlayHealth.Evaluate(Healthy with { TimerTicking = false }));
        Assert.Equal(OverlayProblem.Transparent, OverlayHealth.Evaluate(Healthy with { Opacity = 0.0 }));
    }

    [Fact]
    public void Evaluate_SeveralFaults_AreCombined()
    {
        var problems = OverlayHealth.Evaluate(Healthy with { Visible = false, TimerTicking = false });

        Assert.Equal(OverlayProblem.Hidden | OverlayProblem.TimerStalled, problems);
    }

    [Fact]
    public void Evaluate_OpacityJustBelowFull_CountsAsTransparent()
    {
        Assert.Equal(OverlayProblem.Transparent, OverlayHealth.Evaluate(Healthy with { Opacity = 0.99 }));
    }
}
