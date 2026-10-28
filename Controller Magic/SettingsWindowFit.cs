namespace ControllerMagic;

// How tall the Settings window's page area is, and whether it must scroll.
internal readonly record struct SettingsWindowFit(int BodyHeight, bool Scrolls)
{
    // The window is as tall as its tallest page; it only scrolls if that would not fit the screen.
    public static SettingsWindowFit Compute(int tallestContent, int chromeHeight, int workAreaHeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(tallestContent);
        ArgumentOutOfRangeException.ThrowIfNegative(chromeHeight);

        int available = Math.Max(0, workAreaHeight - chromeHeight);
        return tallestContent <= available ? new(tallestContent, false) : new(available, true);
    }
}
