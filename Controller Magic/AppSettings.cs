using System.Text.Json;

namespace ControllerMagic
{
    internal sealed class AppSettings
    {
        public static AppSettings Instance { get; } = Load();

        // All deadzone modifiers
        public int StickDeadZone { get; set; } = 4000;
        public int ScrollDeadZone { get; set; } = 4000;
        public int KeyboardDeadZone { get; set; } = 4000;

        // Stick sensitivity
        public float StickSensitivity { get; set; } = 0.02f;

        public bool RunAtStartup { get; set; } = false;

        public float StickAccelPower { get; set; } = 0.1f;

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
            catch
            {
            }

            return new AppSettings();
        }

        public void Save()
        {
            try
            {
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true
                };
                string json = JsonSerializer.Serialize(this, options);
                File.WriteAllText(SettingsPath, json);
            }
            catch
            {
            }
        }
    }
}
