using ControllerMagic;
using Xunit;

namespace ControllerMagic.Tests;

public class ResourceUsageMonitorTests
{
    [Theory]
    [InlineData(null, 30u, true)]
    [InlineData(30u, 30u, false)]
    [InlineData(30u, 54u, false)]
    [InlineData(30u, 55u, true)]
    [InlineData(55u, 30u, true)]
    [InlineData(55u, 31u, false)]
    public void HasMoved_OnlyAFirstReadingOrAStepOf25(uint? logged, uint current, bool expected) =>
        Assert.Equal(expected, ResourceUsageMonitor.HasMoved(logged, current));
}
