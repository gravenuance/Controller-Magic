using System.Windows.Forms;
using ControllerMagic;
using Xunit;

namespace ControllerMagic.Tests;

public sealed class SettingsFormTests
{
    private static List<T> Descendants<T>(Control root) where T : Control
    {
        var found = new List<T>();
        foreach (Control child in root.Controls)
        {
            if (child is T match)
                found.Add(match);
            found.AddRange(Descendants<T>(child));
        }
        return found;
    }

    [Fact]
    public void Build_HasOnePageForEveryNavigationItem_AndShowsOnlyTheSavedOne()
    {
        using var poller = new ControllerPoller(new InputEmulator(new FakeDesktopInput()));
        using var form = new SettingsForm(poller);

        var nav = Assert.Single(Descendants<NavList>(form));
        var cards = Descendants<CardPanel>(form);

        Assert.Equal(Enum.GetValues<SettingsPage>().Length, cards.Count);
        Assert.Equal(cards.Count, nav.AccessibilityObject.GetChildCount());
        Assert.Equal(SettingsPage.General, form.ShownPage);
        Assert.Equal(0, nav.SelectedIndex);
    }

    [Fact]
    public void SelectingAPage_ShowsThatPageAndRemembersIt()
    {
        using var poller = new ControllerPoller(new InputEmulator(new FakeDesktopInput()));
        using var form = new SettingsForm(poller);
        var nav = Assert.Single(Descendants<NavList>(form));

        try
        {
            nav.SelectedIndex = (int)SettingsPage.Mouse;

            Assert.Equal(SettingsPage.Mouse, form.ShownPage);
            Assert.Equal(SettingsPage.Mouse, AppSettings.Instance.LastSettingsPage);
        }
        finally
        {
            AppSettings.Instance.LastSettingsPage = SettingsPage.General;
        }
    }

    [Fact]
    public void Window_IsAsTallAsItsTallestPage_SoNoPageNeedsToScroll()
    {
        using var poller = new ControllerPoller(new InputEmulator(new FakeDesktopInput()));
        using var form = new SettingsForm(poller);

        var host = Assert.Single(Descendants<NavList>(form)).Parent!.Controls.OfType<Panel>().Single(panel => panel.AccessibleName == "Settings page");
        int tallestCard = Descendants<CardPanel>(form).Max(card => card.Height);

        Assert.False(host.AutoScroll);
        Assert.True(host.Height >= tallestCard);
        Assert.Equal(host.Bottom, form.ClientSize.Height);
    }
}
