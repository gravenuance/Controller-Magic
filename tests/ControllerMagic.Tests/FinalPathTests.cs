using System.Diagnostics;
using ControllerMagic;
using Xunit;

namespace ControllerMagic.Tests;

public sealed class FinalPathTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("ControllerMagic.FinalPath.");

    public void Dispose()
    {
        // Deleting the junction first, without recursing, leaves its target to be deleted once.
        string junction = Path.Combine(_root.FullName, "link");
        if (Directory.Exists(junction))
            Directory.Delete(junction);
        _root.Delete(recursive: true);
    }

    // A junction, like the D:\Tools install folder; mklink /J needs no elevation or developer mode.
    private (string Real, string ThroughJunction) ExeBehindAJunction()
    {
        string realDir = Directory.CreateDirectory(Path.Combine(_root.FullName, "real")).FullName;
        string real = Path.Combine(realDir, "app.exe");
        File.WriteAllText(real, "x");
        string junction = Path.Combine(_root.FullName, "link");

        var start = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
        foreach (string arg in new[] { "/c", "mklink", "/J", junction, realDir })
            start.ArgumentList.Add(arg);
        using var mklink = Process.Start(start)!;
        mklink.WaitForExit();
        Assert.Equal(0, mklink.ExitCode);

        return (real, Path.Combine(junction, "app.exe"));
    }

    // Regression: launching through a link re-pointed the startup task at the link's path.
    [Fact]
    public void IsSameExecutable_PathThroughAJunctionAndItsTarget_AreTheSameFile()
    {
        var (real, throughJunction) = ExeBehindAJunction();

        Assert.True(StartupTaskDefinition.IsSameExecutable(throughJunction, real));
    }

    [Fact]
    public void IsSameExecutable_JunctionRemoved_IsNoLongerTheSameFile()
    {
        var (real, throughJunction) = ExeBehindAJunction();
        Directory.Delete(Path.GetDirectoryName(throughJunction)!);

        Assert.False(StartupTaskDefinition.IsSameExecutable(throughJunction, real));
    }

    [Fact]
    public void TryResolve_PathThroughAJunction_ReturnsTheTargetPath()
    {
        var (real, throughJunction) = ExeBehindAJunction();

        Assert.Equal(real, FinalPath.TryResolve(throughJunction), ignoreCase: true);
    }

    [Fact]
    public void TryResolve_MissingFile_IsNull() =>
        Assert.Null(FinalPath.TryResolve(Path.Combine(_root.FullName, "missing.exe")));

    [Theory]
    [InlineData(@"\\?\D:\Tools\app.exe", @"D:\Tools\app.exe")]
    [InlineData(@"\\?\UNC\server\share\app.exe", @"\\server\share\app.exe")]
    [InlineData(@"D:\Tools\app.exe", @"D:\Tools\app.exe")]
    public void StripDevicePrefix_GivesTheUsualForm(string path, string expected) =>
        Assert.Equal(expected, FinalPath.StripDevicePrefix(path));
}
