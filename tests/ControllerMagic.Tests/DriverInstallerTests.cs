using ControllerMagic;
using Xunit;

namespace ControllerMagic.Tests;

public class DriverInstallerTests
{
    private const DriverKinds Both = DriverKinds.HidHide | DriverKinds.Vigem;

    private static int Exit(InstallerStepResult hidHide, InstallerStepResult vigem) =>
        DriverInstallProtocol.EncodeExitCode(hidHide, vigem);

    [Fact]
    public void ToInstallResult_BothSucceeded_ReturnsSuccess()
    {
        var result = DriverInstaller.ToInstallResult(Both, Exit(InstallerStepResult.Succeeded, InstallerStepResult.Succeeded));

        Assert.Equal(InstallOutcome.Success, result.Outcome);
    }

    [Fact]
    public void ToInstallResult_OnlyHidHideRequestedAndSucceeded_ReturnsSuccess()
    {
        var result = DriverInstaller.ToInstallResult(DriverKinds.HidHide, Exit(InstallerStepResult.Succeeded, InstallerStepResult.NotRequested));

        Assert.Equal(InstallOutcome.Success, result.Outcome);
    }

    [Theory]
    [InlineData((int)InstallerStepResult.RebootRequired, (int)InstallerStepResult.Succeeded)]
    [InlineData((int)InstallerStepResult.Succeeded, (int)InstallerStepResult.RebootRequired)]
    public void ToInstallResult_EitherNeedsReboot_ReturnsRebootRequired(int hidHide, int vigem)
    {
        var result = DriverInstaller.ToInstallResult(Both, Exit((InstallerStepResult)hidHide, (InstallerStepResult)vigem));

        Assert.Equal(InstallOutcome.RebootRequired, result.Outcome);
    }

    [Fact]
    public void ToInstallResult_AnyFailure_ReturnsInstallFailedEvenIfTheOtherSucceeded()
    {
        var result = DriverInstaller.ToInstallResult(Both, Exit(InstallerStepResult.Failed, InstallerStepResult.Succeeded));

        Assert.Equal(InstallOutcome.InstallFailed, result.Outcome);
    }

    [Fact]
    public void ToInstallResult_AnySignatureInvalid_ReturnsSignatureVerificationFailed()
    {
        var result = DriverInstaller.ToInstallResult(Both, Exit(InstallerStepResult.Succeeded, InstallerStepResult.SignatureInvalid));

        Assert.Equal(InstallOutcome.SignatureVerificationFailed, result.Outcome);
    }

    [Fact]
    public void ToInstallResult_RequestedDriverReportedAsNotRequested_ReturnsInstallFailedRatherThanAssumingSuccess()
    {
        var result = DriverInstaller.ToInstallResult(Both, Exit(InstallerStepResult.Succeeded, InstallerStepResult.NotRequested));

        Assert.Equal(InstallOutcome.InstallFailed, result.Outcome);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(ElevatedDriverInstall.FailedExitCode)]
    [InlineData(-532462766)] // An unhandled .NET exception's process exit code.
    public void ToInstallResult_ExitCodeOutsideTheProtocol_ReturnsInstallFailed(int exitCode)
    {
        var result = DriverInstaller.ToInstallResult(Both, exitCode);

        Assert.Equal(InstallOutcome.InstallFailed, result.Outcome);
    }

    [Theory]
    [InlineData(0, (int)InstallerStepResult.Succeeded)]
    [InlineData(3010, (int)InstallerStepResult.RebootRequired)]
    [InlineData(1603, (int)InstallerStepResult.Failed)]
    [InlineData(-1, (int)InstallerStepResult.Failed)]
    public void DriverPackages_StepResultFromExitCode_MapsMsiExitCodes(int exitCode, int expected) =>
        Assert.Equal((InstallerStepResult)expected, DriverPackages.StepResultFromExitCode(exitCode));
}
