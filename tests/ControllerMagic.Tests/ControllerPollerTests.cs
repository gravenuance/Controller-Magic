using ControllerMagic;
using Xunit;

namespace ControllerMagic.Tests;

public class ControllerPollerTests
{
    [Fact]
    public void ComputeHoldRamp_RampSecondsZeroOrNegative_AlwaysFullSpeed()
    {
        Assert.Equal(1.0, ControllerPoller.ComputeHoldRamp(0.0, 0.0));
        Assert.Equal(1.0, ControllerPoller.ComputeHoldRamp(5.0, 0.0));
        Assert.Equal(1.0, ControllerPoller.ComputeHoldRamp(0.0, -1.0));
    }

    [Fact]
    public void ComputeHoldRamp_AtZeroHeldSeconds_StartsNearButBelowFullSpeed()
    {
        double ramp = ControllerPoller.ComputeHoldRamp(0.0, 1.0);

        Assert.True(ramp is > 0.0 and < 0.5, $"expected a slow start, got {ramp}");
    }

    [Fact]
    public void ComputeHoldRamp_AtMidpoint_IsHalfSpeed()
    {
        const double rampSeconds = 1.0;
        double ramp = ControllerPoller.ComputeHoldRamp(rampSeconds / 2.0, rampSeconds);

        Assert.Equal(0.5, ramp, precision: 6);
    }

    [Fact]
    public void ComputeHoldRamp_WellPastRampSeconds_ApproachesFullSpeed()
    {
        double ramp = ControllerPoller.ComputeHoldRamp(10.0, 1.0);

        Assert.True(ramp > 0.99, $"expected near-full speed, got {ramp}");
    }

    [Fact]
    public void ComputeHoldRamp_IsMonotonicallyIncreasingWithHeldTime()
    {
        const double rampSeconds = 0.5;
        double previous = 0.0;

        for (double held = 0.0; held <= rampSeconds * 2; held += 0.05)
        {
            double ramp = ControllerPoller.ComputeHoldRamp(held, rampSeconds);
            Assert.True(ramp >= previous, $"ramp decreased at held={held}: {ramp} < {previous}");
            previous = ramp;
        }
    }
}
