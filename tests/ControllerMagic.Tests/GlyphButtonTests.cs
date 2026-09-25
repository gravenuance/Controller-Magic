using System.Windows.Forms;
using ControllerMagic;
using Xunit;

namespace ControllerMagic.Tests;

public class GlyphButtonTests
{
    [Theory]
    [InlineData(Keys.Enter)]
    [InlineData(Keys.Space)]
    public void ActivationKeys_AreEnterAndSpace(Keys key) => Assert.True(GlyphButton.IsActivationKey(key));

    [Theory]
    [InlineData(Keys.Escape)]
    [InlineData(Keys.Tab)]
    [InlineData(Keys.Enter | Keys.Alt)]
    public void OtherKeys_DoNotActivate(Keys key) => Assert.False(GlyphButton.IsActivationKey(key));

    [Fact]
    public void PerformClick_RaisesClick()
    {
        using var button = new GlyphButton();
        int clicks = 0;
        button.Click += (_, _) => clicks++;

        button.PerformClick();

        Assert.Equal(1, clicks);
    }

    [Fact]
    public void PerformClick_WhenDisabled_DoesNothing()
    {
        using var button = new GlyphButton { Enabled = false };
        int clicks = 0;
        button.Click += (_, _) => clicks++;

        button.PerformClick();

        Assert.Equal(0, clicks);
    }

    [Fact]
    public void IsAKeyboardReachablePushButton()
    {
        using var button = new GlyphButton { AccessibleName = "Close" };
        int clicks = 0;
        button.Click += (_, _) => clicks++;

        button.AccessibilityObject.DoDefaultAction();

        Assert.True(button.TabStop);
        Assert.Equal(AccessibleRole.PushButton, button.AccessibilityObject.Role);
        Assert.Equal("Close", button.AccessibilityObject.Name);
        Assert.Equal(1, clicks);
    }

    [Fact]
    public void CanBeAFormsCancelButton()
    {
        using var form = new Form();
        using var button = new GlyphButton();

        form.CancelButton = button;

        Assert.Same(button, form.CancelButton);
    }
}
