using ControllerMagic;
using Xunit;

namespace ControllerMagic.Tests;

public class ControllerPollerStopTests
{
    [Fact]
    public void Stop_PollThreadStuck_GivesUpAndStillUncloaksTheRealPad()
    {
        var harness = new PassthroughHarness();
        harness.TickWithPad();
        using var poller = new ControllerPoller(harness.Controller, new InputEmulator(new FakeDesktopInput()));
        using var release = new ManualResetEventSlim();
        poller.StartThread(() => release.Wait(TestContext.Current.CancellationToken));

        try
        {
            bool joined = poller.Stop(TimeSpan.FromMilliseconds(20));

            Assert.False(joined);
            Assert.False(harness.HidHide.Cloaked);
            Assert.False(harness.VirtualPad.IsConnected);
        }
        finally
        {
            release.Set();
        }
    }

    [Fact]
    public void Stop_PollThreadEnds_ReportsAJoin()
    {
        var harness = new PassthroughHarness();
        using var poller = new ControllerPoller(harness.Controller, new InputEmulator(new FakeDesktopInput()));
        poller.StartThread(() => { });

        Assert.True(poller.Stop(Timeout.InfiniteTimeSpan));
    }
}
