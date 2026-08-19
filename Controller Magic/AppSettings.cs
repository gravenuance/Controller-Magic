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
        public float StickSensitivity { get; set; } = 0.018f;

        public bool RunAtStartup { get; set; } = false;

        // Exponent applied to the normalized stick magnitude (0..1) before scaling cursor speed.
        // >1 gives a gradual ramp - slow/precise near center, faster toward full deflection.
        // <1 does the opposite (snaps to near-max speed on almost any push), which is why the
        // old default of 0.05 felt twitchy instead of smooth.
        public float StickAccelPower { get; set; } = 2.0f;

        // Seconds of continuously holding the stick deflected before cursor speed reaches full
        // ramp-up, following an S-curve (slow start, fast middle, leveling off) rather than an
        // instant jump - lets a quick nudge stay precise while a sustained push still reaches full
        // speed quickly. 0 disables the ramp (speed is driven by deflection alone, as before).
        public float StickRampSeconds { get; set; } = 0.35f;

        // Process names (substring match) whose fullscreen windows still receive
        // controller input instead of being treated as a game and blocked.
        public List<string> WatchedProcessNames { get; set; } = new()
        {
            "firefox", "vlc", "chrome", "explorer", "recorder", "steam"
        };

        // Window-title keywords (substring match) identifying an actual streaming service, where
        // the D-pad's 'S' press is meaningful as a "Skip Intro" shortcut. Deliberately a separate,
        // narrower list from WatchedProcessNames: generic fullscreen apps like VLC or Steam
        // shouldn't get 'S' bound to anything, since it isn't a real shortcut there.
        public List<string> StreamingServiceNames { get; set; } = new()
        {
            "Netflix", "Prime Video", "Disney+", "Hulu", "Max", "Paramount+", "Peacock", "Apple TV"
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
