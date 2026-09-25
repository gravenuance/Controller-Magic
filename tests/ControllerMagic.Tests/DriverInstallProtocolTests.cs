using ControllerMagic;
using Xunit;

namespace ControllerMagic.Tests;

public class DriverInstallProtocolTests
{
    private const string Source = @"C:\Users\Some One\AppData\Local\ControllerMagic\drivers";

    [Theory]
    [InlineData((int)DriverKinds.HidHide)]
    [InlineData((int)DriverKinds.Vigem)]
    [InlineData((int)(DriverKinds.HidHide | DriverKinds.Vigem))]
    public void BuildArguments_RoundTripsThroughTryParse(int drivers)
    {
        var request = new DriverInstallRequest((DriverKinds)drivers, Source);

        var args = DriverInstallProtocol.BuildArguments(request);

        Assert.True(DriverInstallProtocol.IsInstallCommand(args));
        Assert.True(DriverInstallProtocol.TryParse(args, out var parsed));
        Assert.Equal(request, parsed);
    }

    [Fact]
    public void BuildArguments_NoDrivers_Throws() =>
        Assert.Throws<ArgumentException>(() => DriverInstallProtocol.BuildArguments(new DriverInstallRequest(DriverKinds.None, Source)));

    [Theory]
    [InlineData("--install-drivers", "all", "--source", Source)]
    [InlineData("--install-drivers", "HidHide", "--source", Source)]
    [InlineData("--install-drivers", "both", "--src", Source)]
    [InlineData("--install-drivers", "both", "--source", @"relative\drivers")]
    [InlineData("--install-drivers", "both", "--source", @"C:\Users\x\..\..\Windows")]
    [InlineData("--install-drivers", "both", "--source", @"C:\drivers\.")]
    [InlineData("--install-drivers", "both", "--source", "")]
    [InlineData("--startup", "both", "--source", Source)]
    public void TryParse_RejectsAnythingButTheExactShape(string a0, string a1, string a2, string a3) =>
        Assert.False(DriverInstallProtocol.TryParse([a0, a1, a2, a3], out _));

    [Fact]
    public void TryParse_RejectsExtraOrMissingArguments()
    {
        Assert.False(DriverInstallProtocol.TryParse(["--install-drivers", "both", "--source"], out _));
        Assert.False(DriverInstallProtocol.TryParse(["--install-drivers", "both", "--source", Source, "--extra"], out _));
    }

    [Fact]
    public void IsAcceptableSourceDirectory_RejectsOverlongPaths() =>
        Assert.False(DriverInstallProtocol.IsAcceptableSourceDirectory(@"C:\" + new string('a', 2000)));

    [Fact]
    public void IsInstallCommand_OnlyForTheInstallFlagFirst()
    {
        Assert.False(DriverInstallProtocol.IsInstallCommand([]));
        Assert.False(DriverInstallProtocol.IsInstallCommand(["--startup"]));
        Assert.False(DriverInstallProtocol.IsInstallCommand(["--startup", "--install-drivers"]));
    }

    [Fact]
    public void ExitCode_RoundTripsEveryCombination()
    {
        foreach (var hidHide in Enum.GetValues<InstallerStepResult>())
        {
            foreach (var vigem in Enum.GetValues<InstallerStepResult>())
            {
                int code = DriverInstallProtocol.EncodeExitCode(hidHide, vigem);

                Assert.True(DriverInstallProtocol.TryDecodeExitCode(code, out var decodedHidHide, out var decodedVigem));
                Assert.Equal((hidHide, vigem), (decodedHidHide, decodedVigem));
            }
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3010)]
    [InlineData(-1)]
    [InlineData(0x4D43_0009)] // Marker with an undefined step value.
    [InlineData(0x4D43_0111)] // Marker with stray high bits.
    public void TryDecodeExitCode_RejectsCodesOutsideTheProtocol(int exitCode) =>
        Assert.False(DriverInstallProtocol.TryDecodeExitCode(exitCode, out _, out _));
}
