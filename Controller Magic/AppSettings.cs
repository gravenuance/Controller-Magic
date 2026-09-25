namespace ControllerMagic
{
    internal sealed class AppSettings
    {
        // Bumped whenever a persisted field's meaning or shape changes in a way older data can't
        // just deserialize into as-is. 0 (the C# default for a settings.json with no SchemaVersion
        // key at all - every file written before this version existed) always means "predates
        // versioning" and is migrated forward the same way any older explicit version would be.
        internal const int CurrentSchemaVersion = 1;

        public static AppSettings Instance { get; } = SettingsStore.CreateDefault().Load();

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

        // Set by the store that loaded this instance; non-public, so never serialized.
        internal SettingsStore? Store { get; set; }

        internal static AppSettings CreateDefault() => new() { SchemaVersion = CurrentSchemaVersion };

        public void Save()
        {
            if (Store == null)
                throw new InvalidOperationException("Only settings loaded through a SettingsStore can be saved.");

            Store.Save(this);
        }
    }
}
