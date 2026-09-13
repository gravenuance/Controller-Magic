using ControllerMagic;
using Xunit;

namespace ControllerMagic.Tests;

public class DriverDependencyTests
{
    [Theory]
    [InlineData(true, true, true, true)]
    [InlineData(true, true, false, true)]
    [InlineData(true, false, true, true)]
    [InlineData(false, true, true, true)]
    [InlineData(false, false, true, true)]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, false, false, false)]
    public void ShouldToggleBeEnabled_AllCombinations_MatchesInstalledOrNetworkPolicy(
        bool hidHideInstalled, bool vigemInstalled, bool networkAvailable, bool expected)
    {
        var status = new DriverStatus(hidHideInstalled, vigemInstalled, networkAvailable);

        Assert.Equal(expected, DriverDependency.ShouldToggleBeEnabled(status));
    }
}
