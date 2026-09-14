using ControllerMagic;
using Xunit;

namespace ControllerMagic.Tests;

public class DriverInstallerTests
{
    [Fact]
    public void CombineOutcome_BothSucceeded_ReturnsSuccess()
    {
        var result = DriverInstaller.CombineOutcome(hidHideRan: true, hidHideExit: 0, vigemRan: true, vigemExit: 0);

        Assert.Equal(InstallOutcome.Success, result.Outcome);
    }

    [Fact]
    public void CombineOutcome_OnlyHidHideRanAndSucceeded_ReturnsSuccess()
    {
        var result = DriverInstaller.CombineOutcome(hidHideRan: true, hidHideExit: 0, vigemRan: false, vigemExit: null);

        Assert.Equal(InstallOutcome.Success, result.Outcome);
    }

    [Fact]
    public void CombineOutcome_EitherExitCodeIsRebootRequired_ReturnsRebootRequired()
    {
        var hidHideRebooted = DriverInstaller.CombineOutcome(hidHideRan: true, hidHideExit: 3010, vigemRan: true, vigemExit: 0);
        var vigemRebooted = DriverInstaller.CombineOutcome(hidHideRan: true, hidHideExit: 0, vigemRan: true, vigemExit: 3010);

        Assert.Equal(InstallOutcome.RebootRequired, hidHideRebooted.Outcome);
        Assert.Equal(InstallOutcome.RebootRequired, vigemRebooted.Outcome);
    }

    [Fact]
    public void CombineOutcome_AnyGenuineFailureExitCode_ReturnsInstallFailedEvenIfTheOtherSucceeded()
    {
        var result = DriverInstaller.CombineOutcome(hidHideRan: true, hidHideExit: 1603, vigemRan: true, vigemExit: 0);

        Assert.Equal(InstallOutcome.InstallFailed, result.Outcome);
    }

    [Fact]
    public void CombineOutcome_RanButExitCodeUnreadable_ReturnsInstallFailedRatherThanAssumingSuccess()
    {
        var result = DriverInstaller.CombineOutcome(hidHideRan: true, hidHideExit: null, vigemRan: false, vigemExit: null);

        Assert.Equal(InstallOutcome.InstallFailed, result.Outcome);
    }

    [Fact]
    public void CombineOutcome_NeitherRan_ReturnsSuccess()
    {
        var result = DriverInstaller.CombineOutcome(hidHideRan: false, hidHideExit: null, vigemRan: false, vigemExit: null);

        Assert.Equal(InstallOutcome.Success, result.Outcome);
    }
}
