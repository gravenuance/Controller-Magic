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

        public bool RunAtStartup { get; set; }

        // Whether the first-run startup default has already been established, so it's only ever
        // set up once - after that, whatever the user has it set to (on or off) is left alone.
        public bool HasInitializedStartup { get; set; }

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

        // %LocalAppData%, not next to the exe: installs under Program Files (Steam's default
        // library location, for instance) aren't writable by a standard user, which silently broke
        // saving here before. Same folder the crash log already uses.
        private static string SettingsDirectory =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ControllerMagic");

        private static string SettingsPath => Path.Combine(SettingsDirectory, "settings.json");

        private static string LegacySettingsPath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");

        private static AppSettings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                    return LoadFrom(SettingsPath) ?? new AppSettings();

                // One-time migration for installs that still have the old next-to-the-exe file.
                if (File.Exists(LegacySettingsPath))
                {
                    var migrated = LoadFrom(LegacySettingsPath);
                    if (migrated != null)
                    {
                        migrated.Save();
                        TryDeleteLegacyFile();
                        return migrated;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AppSettings] Failed to load settings: {ex}");
            }

            return new AppSettings();
        }

        private static AppSettings? LoadFrom(string path)
        {
            try
            {
                string json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<AppSettings>(json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AppSettings] Failed to read {path}: {ex}");
                return null;
            }
        }

        private static void TryDeleteLegacyFile()
        {
            try
            {
                File.Delete(LegacySettingsPath);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AppSettings] Failed to remove legacy settings file: {ex}");
            }
        }

        private static readonly JsonSerializerOptions SaveOptions = new() { WriteIndented = true };

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(SettingsDirectory);
                string json = JsonSerializer.Serialize(this, SaveOptions);
                File.WriteAllText(SettingsPath, json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AppSettings] Failed to save {SettingsPath}: {ex}");
            }
        }
    }
}
