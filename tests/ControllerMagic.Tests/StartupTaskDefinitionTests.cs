using System.Xml.Linq;
using ControllerMagic;
using Xunit;

namespace ControllerMagic.Tests;

public class StartupTaskDefinitionTests
{
    private const string Exe = @"C:\Program Files\Controller & Magic\Controller Magic.exe";
    private const string UserSid = "S-1-5-21-1-2-3-1001";
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    private static XElement Parse(string xml) => XDocument.Parse(xml).Root!;

    [Fact]
    public void BuildXml_LogonTriggerIsScopedToTheCurrentUser()
    {
        var task = Parse(StartupTaskDefinition.BuildXml(Exe, UserSid));

        var trigger = Assert.Single(task.Element(Ns + "Triggers")!.Elements());
        Assert.Equal(Ns + "LogonTrigger", trigger.Name);
        Assert.Equal(UserSid, trigger.Element(Ns + "UserId")?.Value);
    }

    [Fact]
    public void BuildXml_RunsAsTheUserInteractivelyWithoutElevation()
    {
        var principal = Parse(StartupTaskDefinition.BuildXml(Exe, UserSid)).Element(Ns + "Principals")!.Element(Ns + "Principal")!;

        Assert.Equal(UserSid, principal.Element(Ns + "UserId")?.Value);
        Assert.Equal("InteractiveToken", principal.Element(Ns + "LogonType")?.Value);
        Assert.Equal("LeastPrivilege", principal.Element(Ns + "RunLevel")?.Value);
    }

    [Fact]
    public void BuildXml_NeverStoppedForBatteryOrRunTime()
    {
        var settings = Parse(StartupTaskDefinition.BuildXml(Exe, UserSid)).Element(Ns + "Settings")!;

        Assert.Equal("false", settings.Element(Ns + "DisallowStartIfOnBatteries")?.Value);
        Assert.Equal("false", settings.Element(Ns + "StopIfGoingOnBatteries")?.Value);
        Assert.Equal("PT0S", settings.Element(Ns + "ExecutionTimeLimit")?.Value);
    }

    [Fact]
    public void BuildXml_ActionRunsTheExeWithTheStartupFlag_EscapingThePath()
    {
        string xml = StartupTaskDefinition.BuildXml(Exe, UserSid);
        var exec = Parse(xml).Element(Ns + "Actions")!.Element(Ns + "Exec")!;

        Assert.Contains("&amp;", xml, StringComparison.Ordinal);
        Assert.Equal(Exe, exec.Element(Ns + "Command")?.Value);
        Assert.Equal("--startup", exec.Element(Ns + "Arguments")?.Value);
    }

    [Fact]
    public void NeedsRefresh_TaskBuiltForThisExe_IsFalse() =>
        Assert.False(StartupTaskDefinition.NeedsRefresh(StartupTaskDefinition.BuildXml(Exe, UserSid), Exe));

    [Fact]
    public void NeedsRefresh_ExeMoved_IsTrue() =>
        Assert.True(StartupTaskDefinition.NeedsRefresh(StartupTaskDefinition.BuildXml(@"D:\Old\Controller Magic.exe", UserSid), Exe));

    [Fact]
    public void NeedsRefresh_IgnoresQuotesAndCase()
    {
        string registered = StartupTaskDefinition.BuildXml($"\"{Exe.ToUpperInvariant()}\"", UserSid);

        Assert.False(StartupTaskDefinition.NeedsRefresh(registered, Exe));
    }

    // What "schtasks /Create /SC ONLOGON /TR ..." registered before: a trigger for any user.
    [Fact]
    public void NeedsRefresh_LegacyAnyUserLogonTrigger_IsTrue()
    {
        const string legacy = """
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <Triggers><LogonTrigger><Enabled>true</Enabled></LogonTrigger></Triggers>
              <Actions Context="Author">
                <Exec><Command>"C:\Program Files\Controller &amp; Magic\Controller Magic.exe"</Command><Arguments>--startup</Arguments></Exec>
              </Actions>
            </Task>
            """;

        Assert.True(StartupTaskDefinition.NeedsRefresh(legacy, Exe));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not xml")]
    [InlineData("<Task xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\"/>")]
    public void NeedsRefresh_UnreadableTask_IsTrue(string xml) =>
        Assert.True(StartupTaskDefinition.NeedsRefresh(xml, Exe));

    [Theory]
    [InlineData(@"C:\A\app.exe", @"c:\a\APP.EXE", true)]
    [InlineData(@"""C:\A\app.exe""", @"C:\A\app.exe", true)]
    [InlineData(@"C:\A\..\A\app.exe", @"C:\A\app.exe", true)]
    [InlineData(@"C:\A\app.exe", @"C:\B\app.exe", false)]
    [InlineData("", @"C:\A\app.exe", false)]
    public void IsSameExecutable_ComparesNormalisedPaths(string a, string b, bool expected) =>
        Assert.Equal(expected, StartupTaskDefinition.IsSameExecutable(a, b));
}
