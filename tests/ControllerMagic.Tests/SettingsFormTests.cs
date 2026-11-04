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

    // Creating a control installs a WinForms context whose message loop never runs under the test
    // runner, so awaits that resume on it would hang.
    private static void DetachWinFormsContext() => SynchronizationContext.SetSynchronizationContext(null);

    // The elevated install can't be undone once approved, so hiding the window mid-install must not skip the save.
    [Fact]
    public async Task HidHideInstall_WindowHiddenAfterApproval_StillSavesTheChoiceAndRefreshesDrivers()
    {
        using var poller = new ControllerPoller(new InputEmulator(new FakeDesktopInput()));
        using var form = new SettingsForm(poller);
        using var toggle = new ToggleSwitch { Checked = true };
        DetachWinFormsContext();
        var windowToken = new CancellationTokenSource();
        var installStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var installFinish = new TaskCompletionSource<InstallResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        bool refreshed = false;
        form.DetectDriverStatus = _ => Task.FromResult(new DriverStatus(HidHideInstalled: false, VigemInstalled: false, NetworkAvailable: true));
        form.InstallDrivers = async (_, _, token) =>
        {
            installStarted.TrySetResult();
            return await installFinish.Task.WaitAsync(token).ConfigureAwait(false);
        };
        form.RefreshDriverStatus = () =>
        {
            refreshed = true;
            return Task.CompletedTask;
        };
        bool original = AppSettings.Instance.UseHidHide;
        AppSettings.Instance.UseHidHide = false;

        try
        {
            var apply = form.ApplyHidHideChoiceAsync(toggle, _ => { }, windowToken.Token);
            await installStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken).ConfigureAwait(true);
            await windowToken.CancelAsync().ConfigureAwait(true);
            installFinish.SetResult(new InstallResult(InstallOutcome.Success));
            try
            {
                await apply.ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                // The window is gone, so the outcome is rightly not shown; only the saved state matters.
            }

            Assert.True(AppSettings.Instance.UseHidHide);
            Assert.True(refreshed);
        }
        finally
        {
            AppSettings.Instance.UseHidHide = original;
            windowToken.Dispose();
        }
    }

    [Fact]
    public async Task StartupToggle_WhenTurningOffFails_KeepsTheSavedSettingAndRestoresTheSwitch()
    {
        using var poller = new ControllerPoller(new InputEmulator(new FakeDesktopInput()));
        using var form = new SettingsForm(poller);
        using var toggle = new ToggleSwitch { Checked = false };
        DetachWinFormsContext();
        form.SetStartupEnabled = _ => Task.FromResult(false);
        bool original = AppSettings.Instance.RunAtStartup;
        AppSettings.Instance.RunAtStartup = true;

        try
        {
            await form.ApplyStartupChoiceAsync(toggle).ConfigureAwait(true);

            Assert.True(AppSettings.Instance.RunAtStartup);
            Assert.True(toggle.Checked);
            Assert.False(toggle.Busy);
        }
        finally
        {
            AppSettings.Instance.RunAtStartup = original;
        }
    }

    [Fact]
    public async Task StartupToggle_WhenTurningOffSucceeds_SavesTheSetting()
    {
        using var poller = new ControllerPoller(new InputEmulator(new FakeDesktopInput()));
        using var form = new SettingsForm(poller);
        using var toggle = new ToggleSwitch { Checked = false };
        DetachWinFormsContext();
        form.SetStartupEnabled = _ => Task.FromResult(true);
        bool original = AppSettings.Instance.RunAtStartup;
        AppSettings.Instance.RunAtStartup = true;

        try
        {
            await form.ApplyStartupChoiceAsync(toggle).ConfigureAwait(true);

            Assert.False(AppSettings.Instance.RunAtStartup);
            Assert.False(toggle.Checked);
        }
        finally
        {
            AppSettings.Instance.RunAtStartup = original;
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
