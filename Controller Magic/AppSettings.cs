using System.Diagnostics;
using System.Text.Json;

namespace ControllerMagic
{
    internal sealed class AppSettings
    {
        public static AppSettings Instance { get; } = Load();

        // All deadzone modifiers
        public int StickDeadZone { get; set; } = 4000;
        public int ScrollDeadZone { get; set; } = 4000;
        public int KeyboardDeadZone { get; set; } = 6000;

        // Stick sensitivity
        public float StickSensitivity { get; set; } = 0.015f;

        public bool RunAtStartup { get; set; } = false;

        // Exponent applied to the normalized stick magnitude (0..1) before scaling cursor speed.
        // >1 gives a gradual ramp - slow/precise near center, faster toward full deflection.
        // <1 does the opposite (snaps to near-max speed on almost any push), which is why the
        // old default of 0.05 felt twitchy instead of smooth.
        public float StickAccelPower { get; set; } = 2.0f;

        // Process names (substring match) whose fullscreen windows still receive
        // controller input instead of being treated as a game and blocked.
        public List<string> WatchedProcessNames { get; set; } = new()
        {
            "firefox", "vlc", "chrome", "explorer", "recorder", "steam"
        };

        private static string SettingsPath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");

        private static AppSettings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    string json = File.ReadAllText(SettingsPath);
                    var loaded = JsonSerializer.Deserialize<AppSettings>(json);
                    if (loaded != null)
                        return loaded;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AppSettings] Failed to load {SettingsPath}: {ex}");
            }

            return new AppSettings();
        }

        public void Save()
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(this, options);
                File.WriteAllText(SettingsPath, json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AppSettings] Failed to save {SettingsPath}: {ex}");
            }
        }
    }
}
