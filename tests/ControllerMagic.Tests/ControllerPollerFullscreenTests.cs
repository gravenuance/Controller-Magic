using ControllerMagic;
using Xunit;

namespace ControllerMagic.Tests;

public class ControllerPollerFullscreenTests
{
    [Fact]
    public void StandDownForFullscreen_WhilePassthroughActive_UncloaksTheRealPadAndRemovesTheVirtualOne()
    {
        var harness = new PassthroughHarness();
        using var poller = new ControllerPoller(harness.Controller, new InputEmulator(new RecordingInputSink()));
        harness.TickWithPad();

        poller.StandDownForFullscreen();

        Assert.False(harness.HidHide.Cloaked);
        Assert.False(harness.VirtualPad.IsConnected);
    }
}
