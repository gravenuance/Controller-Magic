using ControllerMagic;
using Xunit;

namespace ControllerMagic.Tests;

public class SettingsWindowFitTests
{
    [Fact]
    public void Compute_ContentFits_WindowIsAsTallAsTheTallestPage()
    {
        var fit = SettingsWindowFit.Compute(tallestContent: 300, chromeHeight: 74, workAreaHeight: 1040);

        Assert.Equal(300, fit.BodyHeight);
        Assert.False(fit.Scrolls);
    }

    [Fact]
    public void Compute_ContentExactlyFills_DoesNotScroll()
    {
        var fit = SettingsWindowFit.Compute(tallestContent: 966, chromeHeight: 74, workAreaHeight: 1040);

        Assert.Equal(966, fit.BodyHeight);
        Assert.False(fit.Scrolls);
    }

    [Fact]
    public void Compute_ContentTooTall_ShrinksToTheScreenAndScrolls()
    {
        var fit = SettingsWindowFit.Compute(tallestContent: 700, chromeHeight: 74, workAreaHeight: 600);

        Assert.Equal(526, fit.BodyHeight);
        Assert.True(fit.Scrolls);
    }

    [Fact]
    public void Compute_ScreenShorterThanTheChrome_GivesAZeroHeightBodyThatScrolls()
    {
        var fit = SettingsWindowFit.Compute(tallestContent: 300, chromeHeight: 74, workAreaHeight: 50);

        Assert.Equal(0, fit.BodyHeight);
        Assert.True(fit.Scrolls);
    }
}
