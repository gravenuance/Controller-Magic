using ControllerMagic;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace ControllerMagic.Tests;

public class RepeatingFaultThrottleTests
{
    private readonly FakeTimeProvider _clock = new();

    private static Exception Thrown(Func<Exception> make)
    {
        try
        {
            throw make();
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    private static Exception SameFault() => Thrown(() => new InvalidOperationException("same"));

    [Fact]
    public void Record_FirstFault_IsReportedWithDetails()
    {
        var throttle = new RepeatingFaultThrottle(_clock);

        Assert.Equal(new FaultReport(IsNew: true, Repeats: 0), throttle.Record(SameFault()));
    }

    [Fact]
    public void Record_SameFaultEveryTick_StaysQuietThenReportsTheCountAMinuteLater()
    {
        var throttle = new RepeatingFaultThrottle(_clock);
        throttle.Record(SameFault());

        for (int i = 0; i < 100; i++)
        {
            _clock.Advance(TimeSpan.FromMilliseconds(8));
            Assert.Null(throttle.Record(SameFault()));
        }

        _clock.Advance(TimeSpan.FromMinutes(1));

        Assert.Equal(new FaultReport(IsNew: false, Repeats: 101), throttle.Record(SameFault()));
    }

    [Fact]
    public void Record_DifferentFault_IsReportedWithDetailsStraightAway()
    {
        var throttle = new RepeatingFaultThrottle(_clock);
        throttle.Record(SameFault());

        var report = throttle.Record(Thrown(() => new ArgumentException("other")));

        Assert.Equal(new FaultReport(IsNew: true, Repeats: 0), report);
    }
}
