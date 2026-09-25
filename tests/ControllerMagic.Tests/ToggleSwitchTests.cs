using ControllerMagic;
using Xunit;

namespace ControllerMagic.Tests;

public class ToggleSwitchTests
{
    [Fact]
    public void SettingChecked_DoesNotRaiseToggled()
    {
        using var toggle = new ToggleSwitch();
        int toggled = 0;
        toggle.Toggled += (_, _) => toggled++;

        toggle.Checked = true;
        toggle.Checked = false;

        Assert.Equal(0, toggled);
    }

    [Fact]
    public void ToggleByUser_FlipsCheckedAndRaisesToggled()
    {
        using var toggle = new ToggleSwitch();
        bool? seen = null;
        toggle.Toggled += (_, _) => seen = toggle.Checked;

        toggle.ToggleByUser();

        Assert.True(toggle.Checked);
        Assert.True(seen);
    }

    [Fact]
    public void ToggleByUser_WhileBusy_IsIgnored()
    {
        using var toggle = new ToggleSwitch { Busy = true };
        int toggled = 0;
        toggle.Toggled += (_, _) => toggled++;

        toggle.ToggleByUser();

        Assert.False(toggle.Checked);
        Assert.Equal(0, toggled);
    }
}
