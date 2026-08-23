namespace ControllerMagic
{
    // Single source of truth for the app's "instrument panel" identity: warm amber accent
    // (calibration dials, VU meters) on a graphite-blue ground, replacing the old flat
    // black-and-neon-lime look. Deliberately not theme-adaptive - the app has always rendered
    // dark regardless of the Windows theme, matching most gaming-adjacent tray tools.
    internal static class Theme
    {
        public static readonly Color Bg = Color.FromArgb(0x14, 0x16, 0x1A);
        public static readonly Color Surface = Color.FromArgb(0x1B, 0x1E, 0x24);
        public static readonly Color Surface2 = Color.FromArgb(0x22, 0x26, 0x2D);
        public static readonly Color Line = Color.FromArgb(0x2C, 0x30, 0x38);
        public static readonly Color Ink = Color.FromArgb(0xE8, 0xEA, 0xED);
        public static readonly Color Muted = Color.FromArgb(0x86, 0x8F, 0xA0);
        public static readonly Color Accent = Color.FromArgb(0xE8, 0xA3, 0x3D);
        public static readonly Color Good = Color.FromArgb(0x5F, 0xB8, 0x8A);
        public static readonly Color TitlebarBg = Color.FromArgb(0x17, 0x19, 0x1E);

        public static readonly Font UiFont = new("Segoe UI", 9.5f, FontStyle.Regular);
        public static readonly Font UiFontBold = new("Segoe UI Semibold", 9.5f, FontStyle.Regular);
        public static readonly Font BodyFont = new("Segoe UI", 8.5f, FontStyle.Regular);
        public static readonly Font CaptionFont = new("Segoe UI Semibold", 7.75f, FontStyle.Regular);
        public static readonly Font MonoFont = new("Consolas", 8.75f, FontStyle.Regular);
        public static readonly Font EmojiFont = new("Segoe UI Emoji", 10f, FontStyle.Regular);
    }
}
