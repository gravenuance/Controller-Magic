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
        public static readonly Font GlyphFont = new("Segoe UI", 9f, FontStyle.Regular);

        // Segoe MDL2 Assets/Fluent Icons - Windows' own icon glyph font, bundled since Windows 10.
        // Single-color line-style glyphs at a small size read as "icon", not "text", which is what
        // the tray menu's item icons want without needing to ship or hand-draw bitmap assets.
        public static readonly Font IconFont = new("Segoe MDL2 Assets", 10f, FontStyle.Regular);

        // Properties.Resources.Controller deserializes a brand-new native icon (a real GDI/USER
        // handle) from the embedded .resx data on every access - it's a resource *getter*, not a
        // cached singleton. Form.Icon doesn't take ownership of what's assigned to it either, so
        // every place that used to read the resource directly (the tray icon once at startup, but
        // also SettingsForm's window icon on every single Settings open) was leaking one native
        // icon handle per access, with nothing left to ever dispose it. Loading it once here and
        // sharing that one instance for the app's lifetime - the same pattern already used for the
        // fonts above - fixes that at the root instead of chasing down a Dispose() call per site.
        public static readonly Icon AppIcon = ControllerMagic.Properties.Resources.Controller;
    }
}
