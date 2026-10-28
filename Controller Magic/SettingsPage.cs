using System.Text.Json.Serialization;

namespace ControllerMagic;

// The Settings window's pages, in navigation order. Persisted by name, so renaming or removing a
// member needs a settings migration; an unknown name loads as General.
[JsonConverter(typeof(JsonStringEnumConverter))]
internal enum SettingsPage
{
    General,
    GuideButton,
    Mouse,
    Deadzones,
    Controller,
    FullScreenApps,
}

internal static class SettingsPages
{
    public const SettingsPage Default = SettingsPage.General;

    // Glyphs are Segoe MDL2 Assets code points, which Segoe Fluent Icons also has.
    public static string Title(SettingsPage page) => page switch
    {
        SettingsPage.General => "General",
        SettingsPage.GuideButton => "Guide button",
        SettingsPage.Mouse => "Mouse movement",
        SettingsPage.Deadzones => "Other deadzones",
        SettingsPage.Controller => "Controller",
        SettingsPage.FullScreenApps => "Full-screen apps",
        _ => throw new ArgumentOutOfRangeException(nameof(page), page, null),
    };

    public static string Glyph(SettingsPage page) => page switch
    {
        SettingsPage.General => "",
        SettingsPage.GuideButton => "",
        SettingsPage.Mouse => "",
        SettingsPage.Deadzones => "",
        SettingsPage.Controller => "",
        SettingsPage.FullScreenApps => "",
        _ => throw new ArgumentOutOfRangeException(nameof(page), page, null),
    };

    public static bool IsKnown(SettingsPage page) => Enum.IsDefined(page);
}
