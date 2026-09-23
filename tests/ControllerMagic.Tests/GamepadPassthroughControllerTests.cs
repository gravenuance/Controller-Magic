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
}

public class PassthroughTargetTests
{
    [Fact]
    public void ComputeTarget_SettingOnWithNoControllerConnected_KeepsHidingOnSoAnArrivingPadIsHiddenFromTheStart()
    {
        var target = GamepadPassthroughController.ComputeTarget(
            settingOn: true, driversReady: true, fullscreenSuspended: false,
            padConnected: false, connectionKnown: false, connectionBlocked: false);

        Assert.True(target.Hiding);
        Assert.False(target.VirtualPad);
        Assert.False(target.BlockConnection);
    }

    [Fact]
    public void ComputeTarget_NewUnblockedConnection_BlocksItAndConnectsTheVirtualPad()
    {
        var target = GamepadPassthroughController.ComputeTarget(
            settingOn: true, driversReady: true, fullscreenSuspended: false,
            padConnected: true, connectionKnown: true, connectionBlocked: false);

        Assert.Equal(new PassthroughTarget(Hiding: true, VirtualPad: true, BlockConnection: true), target);
    }

    [Fact]
    public void ComputeTarget_ConnectionAlreadyBlocked_DoesNotBlockAgain()
    {
        var target = GamepadPassthroughController.ComputeTarget(
            settingOn: true, driversReady: true, fullscreenSuspended: false,
            padConnected: true, connectionKnown: true, connectionBlocked: true);

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
            padConnected: true, connectionKnown: true, connectionBlocked: false);

        Assert.Equal(default, target);
    }

    [Fact]
    public void ComputeTarget_PadReadButIdentityUnknown_NoVirtualPadSinceTheRealOneCantBeHidden()
    {
        var target = GamepadPassthroughController.ComputeTarget(
            settingOn: true, driversReady: true, fullscreenSuspended: false,
            padConnected: true, connectionKnown: false, connectionBlocked: false);

        Assert.True(target.Hiding);
        Assert.False(target.VirtualPad);
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
