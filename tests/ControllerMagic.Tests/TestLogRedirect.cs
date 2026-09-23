using System.Runtime.CompilerServices;
using ControllerMagic;

namespace ControllerMagic.Tests;

// Code under test logs through AppLog.Default; without this, a test run appends to the real app.log.
internal static class TestLogRedirect
{
    [ModuleInitializer]
    internal static void RedirectAppLog() =>
        AppLog.Default = new AppLog(
            Path.Combine(Path.GetTempPath(), "ControllerMagic.Tests.log"),
            TimeProvider.System);
}
