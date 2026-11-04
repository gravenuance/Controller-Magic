using ControllerMagic;
using Xunit;

namespace ControllerMagic.Tests;

public class GamepadPassthroughControllerTests
{
    [Theory]
    [InlineData(true, true, false, true)]
    [InlineData(true, true, true, false)]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, false, false, false)]
    [InlineData(false, true, true, false)]
    [InlineData(false, false, true, false)]
    [InlineData(true, false, true, false)]
    public void ComputeShouldBeActive_AllCombinations_RequiresSettingOnDriversReadyAndNotSuspended(
        bool settingOn, bool driversReady, bool fullscreenSuspended, bool expected)
    {
        bool actual = GamepadPassthroughController.ComputeShouldBeActive(settingOn, driversReady, fullscreenSuspended);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Tick_SettingOnWithPad_BlocksCloaksAndConnectsTheVirtualPad()
    {
        var harness = new PassthroughHarness();

        harness.TickWithPad();

        Assert.Equal([PassthroughHarness.Device.InterfacePath], harness.HidHide.BlockedPaths);
        Assert.True(harness.HidHide.Cloaked);
        Assert.True(harness.VirtualPad.IsConnected);
    }

    // Regression: a failed cloak was recorded as done, leaving games two controllers and no retry.
    [Fact]
    public void Tick_CloakFails_NoVirtualPadIsLeftBesideTheVisibleRealOneAndItRetriesAfterADelay()
    {
        var harness = new PassthroughHarness { SettingOn = false };
        harness.TickWithoutPad();
        harness.SettingOn = true;
        harness.HidHide.FailCloakChanges = 1;

        harness.TickWithPad();
        bool virtualPadAfterFailure = harness.VirtualPad.IsConnected;
        int attempts = harness.HidHide.CloakChangeAttempts;
        harness.TickWithPad();
        int attemptsBeforeDelay = harness.HidHide.CloakChangeAttempts;
        harness.Clock.Advance(GamepadPassthroughController.CloakRetryDelay);
        harness.TickWithPad();

        Assert.False(virtualPadAfterFailure);
        Assert.Equal(attempts, attemptsBeforeDelay);
        Assert.True(harness.HidHide.Cloaked);
        Assert.True(harness.VirtualPad.IsConnected);
    }

    // Regression: a failed uncloak for a fullscreen game was recorded as done, so the game never got the pad.
    [Fact]
    public void Tick_UncloakForFullscreenFails_RetriesUntilThePadIsVisible()
    {
        var harness = new PassthroughHarness();
        harness.TickWithPad();
        harness.Controller.SetFullscreenSuspended(true);
        harness.HidHide.FailCloakChanges = 2;

        harness.TickWithoutPad();
        harness.Clock.Advance(GamepadPassthroughController.CloakRetryDelay);
        harness.TickWithoutPad();
        bool hiddenAfterTwoFailures = harness.HidHide.Cloaked;
        harness.Clock.Advance(GamepadPassthroughController.CloakRetryDelay);
        harness.TickWithoutPad();
        bool hiddenBeforeBackoffEnds = harness.HidHide.Cloaked;
        harness.Clock.Advance(GamepadPassthroughController.CloakRetryDelay);
        harness.TickWithoutPad();

        Assert.True(hiddenAfterTwoFailures);
        Assert.True(hiddenBeforeBackoffEnds);
        Assert.False(harness.HidHide.Cloaked);
    }

    [Fact]
    public void ReconnectNeeded_PadConnectedBeforeHiding_IsSet()
    {
        var harness = new PassthroughHarness();

        harness.TickWithPad();

        Assert.True(harness.Controller.ReconnectNeeded);
    }

    [Fact]
    public void ReconnectNeeded_PadReconnectsWhileHidden_IsCleared()
    {
        var harness = new PassthroughHarness();
        harness.TickWithPad();

        harness.TickWithoutPad();
        harness.Controller.Tick(default, gotPad: true, PassthroughHarness.Device, PassthroughHarness.Serial + 1);

        Assert.False(harness.Controller.ReconnectNeeded);
    }

    [Fact]
    public void ReconnectNeeded_BlockFailed_IsNotSet()
    {
        var harness = new PassthroughHarness();
        harness.HidHide.NextBlockResult = BlockResult.Failed;

        harness.TickWithPad();

        Assert.False(harness.Controller.ReconnectNeeded);
    }

    [Fact]
    public void ReconnectNeeded_NoPadEverConnected_IsNotSet()
    {
        var harness = new PassthroughHarness();

        harness.TickWithoutPad();

        Assert.False(harness.Controller.ReconnectNeeded);
    }

    [Fact]
    public void Tick_VirtualPadFailsToConnect_RealPadIsNotLeftHidden()
    {
        var harness = new PassthroughHarness();
        harness.VirtualPad.ConnectSucceeds = false;
        harness.TickWithoutPad();
        Assert.True(harness.HidHide.Cloaked);

        harness.TickWithPad();
        harness.TickWithPad();

        Assert.False(harness.HidHide.Cloaked);
        Assert.False(harness.VirtualPad.IsConnected);
    }

    [Fact]
    public void Tick_VirtualPadFailedOnce_RetriesAfterABackoffAndHidesOnceItConnects()
    {
        var harness = new PassthroughHarness();
        harness.VirtualPad.ConnectSucceeds = false;
        harness.TickWithPad();
        harness.VirtualPad.ConnectSucceeds = true;

        harness.TickWithPad();
        Assert.Equal(1, harness.VirtualPad.ConnectAttempts);

        harness.Clock.Advance(TimeSpan.FromSeconds(1));
        harness.TickWithPad();

        Assert.Equal(2, harness.VirtualPad.ConnectAttempts);
        Assert.True(harness.VirtualPad.IsConnected);
        Assert.True(harness.HidHide.Cloaked);
    }

    [Fact]
    public void Tick_VirtualPadDropsWhileInUse_UncloaksThenReconnectsAfterABackoff()
    {
        var harness = new PassthroughHarness();
        harness.TickWithPad();

        harness.VirtualPad.DropConnection();
        harness.TickWithPad();
        harness.TickWithPad();
        Assert.False(harness.HidHide.Cloaked);

        harness.Clock.Advance(TimeSpan.FromSeconds(1));
        harness.TickWithPad();

        Assert.Equal(2, harness.VirtualPad.ConnectAttempts);
        Assert.True(harness.VirtualPad.IsConnected);
        Assert.True(harness.HidHide.Cloaked);
    }

    [Fact]
    public void Tick_VirtualPadDropsRightAfterEveryConnect_StillGivesUpAfterMaxAttempts()
    {
        var harness = new PassthroughHarness();

        for (int i = 0; i < 20; i++)
        {
            harness.TickWithPad();
            harness.VirtualPad.DropConnection();
            harness.TickWithPad();
            harness.Clock.Advance(TimeSpan.FromSeconds(20));
        }

        Assert.Equal(VirtualPadRetry.MaxAttempts, harness.VirtualPad.ConnectAttempts);
    }

    [Fact]
    public void Tick_VirtualPadDropsAfterStayingUp_StartsTheBackoffAfresh()
    {
        var harness = new PassthroughHarness();
        for (int i = 0; i < 3; i++)
        {
            harness.TickWithPad();
            harness.VirtualPad.DropConnection();
            harness.TickWithPad();
            harness.Clock.Advance(TimeSpan.FromSeconds(20));
        }

        harness.TickWithPad();
        harness.Clock.Advance(TimeSpan.FromMinutes(5));
        harness.VirtualPad.DropConnection();
        harness.TickWithPad();
        harness.Clock.Advance(TimeSpan.FromSeconds(1));
        harness.TickWithPad();

        Assert.Equal(5, harness.VirtualPad.ConnectAttempts);
        Assert.True(harness.VirtualPad.IsConnected);
    }

    [Fact]
    public void Shutdown_WithATransitionStillPending_ItCannotConnectTheVirtualPadAfterwards()
    {
        var harness = new PassthroughHarness();
        harness.TickWithoutPad();
        harness.Runner.Hold = true;
        harness.TickWithPad();

        harness.Controller.Shutdown();
        harness.Runner.RunHeld();

        Assert.False(harness.VirtualPad.IsConnected);
        Assert.False(harness.HidHide.Cloaked);
    }

    [Fact]
    public void Shutdown_WithATransitionStillPending_ItCannotCloakTheRealPadAfterwards()
    {
        var harness = new PassthroughHarness { SettingOn = false };
        harness.TickWithoutPad();
        harness.SettingOn = true;
        harness.Runner.Hold = true;
        harness.TickWithoutPad();

        harness.Controller.Shutdown();
        harness.Runner.RunHeld();

        Assert.False(harness.HidHide.Cloaked);
    }

    // Regression: every launch and exit turned off HidHide cloaking another tool had turned on.
    [Fact]
    public void FirstTickAndShutdown_SettingOff_LeaveAnotherToolsCloakingOn()
    {
        var harness = new PassthroughHarness { SettingOn = false };
        harness.HidHide.Cloaked = true;

        harness.TickWithoutPad();
        bool afterStart = harness.HidHide.Cloaked;
        harness.Controller.Shutdown();

        Assert.True(afterStart);
        Assert.True(harness.HidHide.Cloaked);
    }

    [Fact]
    public void FirstTick_SettingOn_ClearsCloakingAPreviousRunLeftOn()
    {
        var harness = new PassthroughHarness();
        harness.HidHide.Cloaked = true;
        harness.Runner.Hold = true;

        harness.TickWithoutPad();
        harness.Runner.RunHeld();

        Assert.False(harness.HidHide.Cloaked);
    }

    // Regression: the startup uncloak ran alongside the first cloak and could land after it.
    [Fact]
    public void FirstTick_StartupUncloakStillRunning_NoCloakStartsUntilItFinishes()
    {
        var harness = new PassthroughHarness();
        harness.Controller.RefreshDriverStatus();
        harness.Runner.Hold = true;
        harness.Runner.HeldWorkPending = true;

        harness.TickWithoutPad();
        harness.TickWithoutPad();
        int heldBeforeReset = harness.Runner.HeldCount;
        harness.Runner.RunHeld();
        harness.TickWithoutPad();
        harness.Runner.RunHeld();

        Assert.Equal(1, heldBeforeReset);
        Assert.True(harness.HidHide.Cloaked);
    }

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    public void AllowListBeforeDeviceScan_OnlyWithTheSettingOn_ListsTheApp(bool settingOn, int expectedCalls)
    {
        var harness = new PassthroughHarness { SettingOn = settingOn };

        harness.Controller.AllowListBeforeDeviceScan();

        Assert.Equal(expectedCalls, harness.HidHide.AllowListCalls);
    }

    [Fact]
    public void Tick_AfterShutdown_ChangesNothing()
    {
        var harness = new PassthroughHarness();
        harness.TickWithoutPad();
        harness.Controller.Shutdown();

        harness.TickWithPad();

        Assert.False(harness.HidHide.Cloaked);
        Assert.Equal(0, harness.VirtualPad.ConnectAttempts);
    }

    [Fact]
    public void Tick_VirtualPadKeepsFailing_GivesUpUntilTheControllerReconnects()
    {
        var harness = new PassthroughHarness();
        harness.VirtualPad.ConnectSucceeds = false;

        for (int i = 0; i < 20; i++)
        {
            harness.TickWithPad();
            harness.Clock.Advance(TimeSpan.FromMinutes(1));
        }

        Assert.Equal(VirtualPadRetry.MaxAttempts, harness.VirtualPad.ConnectAttempts);
        Assert.False(harness.HidHide.Cloaked);

        harness.Controller.Tick(default, gotPad: true, PassthroughHarness.Device, PassthroughHarness.Serial + 1);

        Assert.Equal(VirtualPadRetry.MaxAttempts + 1, harness.VirtualPad.ConnectAttempts);
    }
}

public class PassthroughTargetTests
{
    [Fact]
    public void ComputeTarget_SettingOnWithNoControllerConnected_KeepsHidingOnSoAnArrivingPadIsHiddenFromTheStart()
    {
        var target = GamepadPassthroughController.ComputeTarget(
            settingOn: true, driversReady: true, fullscreenSuspended: false,
            padConnected: false, connectionKnown: false, connectionBlocked: false,
            virtualPadUnavailable: false);

        Assert.True(target.Hiding);
        Assert.False(target.VirtualPad);
        Assert.False(target.BlockConnection);
    }

    [Fact]
    public void ComputeTarget_NewUnblockedConnection_BlocksItAndConnectsTheVirtualPad()
    {
        var target = GamepadPassthroughController.ComputeTarget(
            settingOn: true, driversReady: true, fullscreenSuspended: false,
            padConnected: true, connectionKnown: true, connectionBlocked: false,
            virtualPadUnavailable: false);

        Assert.Equal(new PassthroughTarget(Hiding: true, VirtualPad: true, BlockConnection: true), target);
    }

    [Fact]
    public void ComputeTarget_ConnectionAlreadyBlocked_DoesNotBlockAgain()
    {
        var target = GamepadPassthroughController.ComputeTarget(
            settingOn: true, driversReady: true, fullscreenSuspended: false,
            padConnected: true, connectionKnown: true, connectionBlocked: true,
            virtualPadUnavailable: false);

        Assert.False(target.BlockConnection);
    }

    [Theory]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public void ComputeTarget_HidingNotWanted_EverythingOff(bool settingOn, bool driversReady, bool fullscreenSuspended)
    {
        var target = GamepadPassthroughController.ComputeTarget(
            settingOn, driversReady, fullscreenSuspended,
            padConnected: true, connectionKnown: true, connectionBlocked: false,
            virtualPadUnavailable: false);

        Assert.Equal(default, target);
    }

    [Fact]
    public void ComputeTarget_PadReadButIdentityUnknown_NoVirtualPadSinceTheRealOneCantBeHidden()
    {
        var target = GamepadPassthroughController.ComputeTarget(
            settingOn: true, driversReady: true, fullscreenSuspended: false,
            padConnected: true, connectionKnown: false, connectionBlocked: false,
            virtualPadUnavailable: false);

        Assert.True(target.Hiding);
        Assert.False(target.VirtualPad);
    }

    [Fact]
    public void ComputeTarget_VirtualPadUnavailableForAConnectedPad_LeavesTheRealPadVisible()
    {
        var target = GamepadPassthroughController.ComputeTarget(
            settingOn: true, driversReady: true, fullscreenSuspended: false,
            padConnected: true, connectionKnown: true, connectionBlocked: false,
            virtualPadUnavailable: true);

        Assert.Equal(default, target);
    }

    [Fact]
    public void ComputeTarget_VirtualPadUnavailableWithNoPad_StillHidesSoAnArrivingPadStartsHidden()
    {
        var target = GamepadPassthroughController.ComputeTarget(
            settingOn: true, driversReady: true, fullscreenSuspended: false,
            padConnected: false, connectionKnown: false, connectionBlocked: false,
            virtualPadUnavailable: true);

        Assert.True(target.Hiding);
    }

    [Theory]
    [InlineData(true, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(false, false, true)]
    public void ShouldAskToReconnect_OnlyAConnectionThatArrivedCloakedAndAlreadyBlockedIsClean(
        bool cloakedAtArrival, bool wasAlreadyBlocked, bool expected)
    {
        Assert.Equal(expected, GamepadPassthroughController.ShouldAskToReconnect(cloakedAtArrival, wasAlreadyBlocked));
    }
}
