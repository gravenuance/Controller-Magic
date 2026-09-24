using System.Text.Json;

namespace ControllerMagic
{
    internal sealed class AppSettings
    {
        // Bumped whenever a persisted field's meaning or shape changes in a way older data can't
        // just deserialize into as-is. 0 (the C# default for a settings.json with no SchemaVersion
        // key at all - every file written before this version existed) always means "predates
        // versioning" and is migrated forward the same way any older explicit version would be.
        internal const int CurrentSchemaVersion = 1;

        public static AppSettings Instance { get; } = Load();

        public int SchemaVersion { get; set; }

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

        // Hides the physical controller (via HidHide) and re-emits it through a virtual pad (via
        // ViGEmBus) so the Guide/Home/Steam button can never reach Xbox Game Bar or Steam's Big
        // Picture mode - neither exposes a config flag for that, so this is the only reliable fix.
        public bool UseHidHide { get; set; }

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

        // Cursor pixels for one finger slide across the full width of a DualSense/DualShock 4 touchpad.
        public int TouchpadSpeed { get; set; } = 1200;

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
                {
                    var raw = LoadFrom(SettingsPath);
                    var resolved = ResolveLoaded(raw);

                    // Persist the migration immediately when the file was older than current (so
                    // it isn't re-migrated every launch) or unreadable (so a corrupt file gets
                    // replaced with valid defaults). A file *newer* than this build understands is
                    // deliberately left untouched on disk - using in-memory defaults for this
                    // session must not overwrite data a future version of the app would still make
                    // sense of, e.g. after a temporary downgrade.
                    if (raw == null || raw.SchemaVersion < CurrentSchemaVersion)
                        resolved.Save();

                    return resolved;
                }

                // One-time migration for installs that still have the old next-to-the-exe file.
                if (File.Exists(LegacySettingsPath))
                {
                    var migrated = ResolveLoaded(LoadFrom(LegacySettingsPath));
                    migrated.Save();
                    TryDeleteLegacyFile();
                    return migrated;
                }
            }
            catch (Exception ex)
            {
                AppLog.Default.Warning("AppSettings: failed to load settings", ex);
            }

            return new AppSettings { SchemaVersion = CurrentSchemaVersion };
        }

        // The versioning policy itself, isolated from file I/O so it's a plain, directly testable
        // function: migrate an older (or pre-versioning, SchemaVersion 0) file forward, or fall
        // back to defaults for a file newer than this build understands, rather than risk
        // misinterpreting a shape it's never seen.
        internal static AppSettings ResolveLoaded(AppSettings? loaded)
        {
            if (loaded == null)
                return new AppSettings { SchemaVersion = CurrentSchemaVersion };

            if (loaded.SchemaVersion > CurrentSchemaVersion)
            {
                AppLog.Default.Warning(
                    $"AppSettings: saved settings are schema version {loaded.SchemaVersion}, newer than " +
                    $"this build understands ({CurrentSchemaVersion}); using defaults instead");
                return new AppSettings { SchemaVersion = CurrentSchemaVersion };
            }

            if (loaded.SchemaVersion < CurrentSchemaVersion)
                loaded.MigrateSchema();

            return loaded;
        }

        // No prior schema version to migrate *from* yet - every field already has the shape this
        // version expects, so this just stamps a legacy (pre-versioning) or older file up to
        // CurrentSchemaVersion. Future bumps add real field migrations here, gated the same way.
        internal void MigrateSchema()
        {
            SchemaVersion = CurrentSchemaVersion;
        }

        internal static AppSettings? LoadFrom(string path)
        {
            try
            {
                string json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<AppSettings>(json);
            }
            catch (Exception ex)
            {
                AppLog.Default.Warning($"AppSettings: failed to read {path}", ex);
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
                AppLog.Default.Warning("AppSettings: failed to remove legacy settings file", ex);
            }
        }

        private static readonly JsonSerializerOptions SaveOptions = new() { WriteIndented = true };

        // Guards the temp-file-then-move sequence below: Save() is called both from a background
        // continuation (TrayApplicationContext's startup init) and from the UI-thread save-debounce
        // timer (SettingsForm), and without this lock two concurrent callers can race on the same
        // fixed temp path - one's File.Move then fails because the other already moved it away.
        private static readonly Lock SaveLock = new();

        public void Save()
        {
            lock (SaveLock)
            {
                try
                {
                    Directory.CreateDirectory(SettingsDirectory);
                    SchemaVersion = CurrentSchemaVersion;
                    string json = JsonSerializer.Serialize(this, SaveOptions);

                    // Atomic: write to a temp file in the same directory, then move it over the real
                    // path. A same-volume File.Move is atomic at the filesystem level, so a crash or
                    // power loss mid-write can never leave settings.json truncated or corrupt - a
                    // reader only ever sees the old file intact or the fully-written new one.
                    string tempPath = SettingsPath + ".tmp";
                    File.WriteAllText(tempPath, json);
                    File.Move(tempPath, SettingsPath, overwrite: true);
                }
                catch (Exception ex)
                {
                    AppLog.Default.Warning($"AppSettings: failed to save {SettingsPath}", ex);
                }
            }
        }
    }
}
