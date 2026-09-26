using System.Runtime.CompilerServices;
using ControllerMagic;

namespace ControllerMagic.Tests;

// Code under test logs through AppLog.Default and reads AppSettings.Instance; without these
// redirects a test run appends to the real app.log and loads and rewrites the real settings.json.
internal static class TestEnvironment
{
    // In range but not the defaults, so a load that alters or resets them is caught.
    internal const int StickDeadZone = 3100;
    internal const int ScrollDeadZone = 3200;
    internal const int KeyboardDeadZone = 5300;
    internal const int TouchpadSpeed = 1400;

    private static readonly string Dir = Path.Combine(Path.GetTempPath(), "ControllerMagic.Tests");

    [ModuleInitializer]
    internal static void Redirect()
    {
        // The log first: the settings store captures AppLog.Default.
        AppLog.Default = new AppLog(Path.Combine(Path.GetTempPath(), "ControllerMagic.Tests.log"), TimeProvider.System);

        string settingsPath = Path.Combine(Dir, "settings.json");
        Directory.CreateDirectory(Dir);
        File.WriteAllText(settingsPath, $$"""
            {
              "SchemaVersion": {{AppSettings.CurrentSchemaVersion}},
              "StickDeadZone": {{StickDeadZone}},
              "ScrollDeadZone": {{ScrollDeadZone}},
              "KeyboardDeadZone": {{KeyboardDeadZone}},
              "TouchpadSpeed": {{TouchpadSpeed}}
            }
            """);
        SettingsStore.Default = new SettingsStore(settingsPath, legacyPath: null, TimeProvider.System, AppLog.Default);
    }
}
