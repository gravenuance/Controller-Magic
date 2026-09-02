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

    [Fact]
    public void ComputeSector_BelowDeadZone_ReturnsNegativeOne()
    {
        Assert.Equal(-1, ControllerPoller.ComputeSector(0, 0, deadZone: 6000));
        Assert.Equal(-1, ControllerPoller.ComputeSector(100, 100, deadZone: 6000));
    }

    [Fact]
    public void ComputeSector_ExactlyAtDeadZoneBoundary_IsNotGated()
    {
        // magSq == deadZone^2 should count as past the deadzone (the check is strictly "<").
        Assert.NotEqual(-1, ControllerPoller.ComputeSector(6000, 0, deadZone: 6000));
    }

    [Theory]
    [InlineData(0, 32767, 0)]     // straight up
    [InlineData(-32767, 0, 2)]    // straight left
    [InlineData(0, -32767, 4)]    // straight down
    [InlineData(32767, 0, 6)]     // straight right
    public void ComputeSector_CardinalDirections_ReturnExpectedSector(short lx, short ly, int expectedSector)
    {
        Assert.Equal(expectedSector, ControllerPoller.ComputeSector(lx, ly, deadZone: 6000));
    }

    [Fact]
    public void ComputeSector_AlwaysReturnsAValidSectorOrNegativeOne()
    {
        for (int angle = 0; angle < 360; angle += 5)
        {
            double rad = angle * Math.PI / 180.0;
            short lx = (short)(20000 * Math.Cos(rad));
            short ly = (short)(20000 * Math.Sin(rad));

            int sector = ControllerPoller.ComputeSector(lx, ly, deadZone: 6000);

            Assert.True(sector is -1 or (>= 0 and < 8), $"angle={angle} produced out-of-range sector {sector}");
        }
    }

    [Fact]
    public void DebounceButtons_FirstReading_IsNotTrustedYet()
    {
        var poller = new ControllerPoller();

        PadButtons result = poller.DebounceButtons(PadButtons.A);

        Assert.Equal(PadButtons.None, result);
    }

    [Fact]
    public void DebounceButtons_TwoConsecutiveIdenticalReadings_BecomesStable()
    {
        var poller = new ControllerPoller();

        poller.DebounceButtons(PadButtons.A);
        PadButtons result = poller.DebounceButtons(PadButtons.A);

        Assert.Equal(PadButtons.A, result);
    }

    [Fact]
    public void DebounceButtons_SingleTickFlicker_DoesNotDisturbAnAlreadyStableValue()
    {
        var poller = new ControllerPoller();
        poller.DebounceButtons(PadButtons.A);
        poller.DebounceButtons(PadButtons.A); // now stable at A

        // A one-tick blip to B, immediately back to A, never repeats B twice in a row - the
        // debounced value should never have visibly changed.
        PadButtons duringFlicker = poller.DebounceButtons(PadButtons.B);
        PadButtons afterFlicker = poller.DebounceButtons(PadButtons.A);

        Assert.Equal(PadButtons.A, duringFlicker);
        Assert.Equal(PadButtons.A, afterFlicker);
    }

    [Fact]
    public void DebounceButtons_SustainedChange_EventuallyBecomesStable()
    {
        var poller = new ControllerPoller();
        poller.DebounceButtons(PadButtons.A);
        poller.DebounceButtons(PadButtons.A); // stable at A

        poller.DebounceButtons(PadButtons.B);
        PadButtons result = poller.DebounceButtons(PadButtons.B); // B held for 2 consecutive polls

        Assert.Equal(PadButtons.B, result);
    }
}
