namespace ControllerMagic
{
    // A setting's allowed range in the Settings slider's integer units; stored value = slider value / Scale.
    internal readonly record struct SettingRange(int Min, int Max, int Scale = 1)
    {
        public int ToSlider(float value) => Math.Clamp((int)Math.Round(value * Scale), Min, Max);

        public float FromSlider(int sliderValue) => (float)sliderValue / Scale;

        public bool Contains(int value) => value >= Min && value <= Max;

        public bool Contains(float value) => float.IsFinite(value) && value >= FromSlider(Min) && value <= FromSlider(Max);

        public int Clamp(int value) => Math.Clamp(value, Min, Max);

        // NaN or infinity falls back to the default rather than an arbitrary end of the range.
        public float Clamp(float value, float fallback) =>
            float.IsFinite(value) ? Math.Clamp(value, FromSlider(Min), FromSlider(Max)) : fallback;
    }

    internal sealed class AppSettings
    {
        // Bumped whenever a persisted field's meaning or shape changes in a way older data can't
        // just deserialize into as-is. 0 (the C# default for a settings.json with no SchemaVersion
        // key at all - every file written before this version existed) always means "predates
        // versioning" and is migrated forward the same way any older explicit version would be.
        internal const int CurrentSchemaVersion = 1;

        // Lazy: loading validates against the ranges below, which an initialiser here would run ahead of.
        private static readonly Lazy<AppSettings> LoadedInstance = new(() => SettingsStore.Default.Load());

        public static AppSettings Instance => LoadedInstance.Value;

        public int SchemaVersion { get; set; }

        // One source of truth for what the Settings sliders offer and what Validate accepts.
        internal static readonly SettingRange StickDeadZoneRange = new(0, 10000);
        internal static readonly SettingRange ScrollDeadZoneRange = new(0, 10000);
        internal static readonly SettingRange KeyboardDeadZoneRange = new(0, 10000);
        internal static readonly SettingRange StickSensitivityRange = new(5, 60, 1000);
        internal static readonly SettingRange StickAccelPowerRange = new(10, 40, 10);
        internal static readonly SettingRange StickRampSecondsRange = new(0, 100, 100);
        internal static readonly SettingRange TouchpadSpeedRange = new(300, 3000);

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

        // Brings hand-edited or damaged values back into range; returns the names of fields it changed.
        internal List<string> Validate()
        {
            var defaults = new AppSettings();
            var corrected = new List<string>();

            StickDeadZone = CheckRange(StickDeadZone, StickDeadZoneRange, nameof(StickDeadZone), corrected);
            ScrollDeadZone = CheckRange(ScrollDeadZone, ScrollDeadZoneRange, nameof(ScrollDeadZone), corrected);
            KeyboardDeadZone = CheckRange(KeyboardDeadZone, KeyboardDeadZoneRange, nameof(KeyboardDeadZone), corrected);
            StickSensitivity = CheckRange(StickSensitivity, defaults.StickSensitivity, StickSensitivityRange, nameof(StickSensitivity), corrected);
            StickAccelPower = CheckRange(StickAccelPower, defaults.StickAccelPower, StickAccelPowerRange, nameof(StickAccelPower), corrected);
            StickRampSeconds = CheckRange(StickRampSeconds, defaults.StickRampSeconds, StickRampSecondsRange, nameof(StickRampSeconds), corrected);
            TouchpadSpeed = CheckRange(TouchpadSpeed, TouchpadSpeedRange, nameof(TouchpadSpeed), corrected);
            WatchedProcessNames = CheckList(WatchedProcessNames, defaults.WatchedProcessNames, nameof(WatchedProcessNames), corrected);
            StreamingServiceNames = CheckList(StreamingServiceNames, defaults.StreamingServiceNames, nameof(StreamingServiceNames), corrected);

            return corrected;
        }

        private static int CheckRange(int value, SettingRange range, string name, List<string> corrected)
        {
            if (range.Contains(value))
                return value;
            corrected.Add(name);
            return range.Clamp(value);
        }

        private static float CheckRange(float value, float fallback, SettingRange range, string name, List<string> corrected)
        {
            if (range.Contains(value))
                return value;
            corrected.Add(name);
            return range.Clamp(value, fallback);
        }

        // JSON null binds straight through the non-nullable annotation, so both the list and its entries are checked.
        private static List<string> CheckList(List<string>? items, List<string> fallback, string name, List<string> corrected)
        {
            if (items == null)
            {
                corrected.Add(name);
                return fallback;
            }

            if (items.TrueForAll(item => !string.IsNullOrWhiteSpace(item)))
                return items;

            corrected.Add(name);
            return items.FindAll(item => !string.IsNullOrWhiteSpace(item));
        }

        public void Save() => RequireStore().Save(this);

        // Batches rapid changes (a slider drag) into one write shortly after they stop.
        public void RequestSave() => RequireStore().RequestSave(this);

        public void FlushPendingSave() => RequireStore().FlushPendingSave();

        private SettingsStore RequireStore() =>
            Store ?? throw new InvalidOperationException("Only settings loaded through a SettingsStore can be saved.");
    }
}
