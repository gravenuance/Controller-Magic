using System.Runtime.InteropServices;

namespace ControllerMagic;

// Names the chosen sound output for a moment, like Windows' own volume popup, without taking focus or clicks.
internal sealed class SoundOutputToast : Form
{
    private static readonly TimeSpan ShownFor = TimeSpan.FromSeconds(1.5);

    private const string SpeakerGlyph = "\uE767";
    private const int SidePadding = 14;
    private const int GlyphGap = 10;
    private const int ToastHeight = 44;
    private const int MaxTextWidth = 600;
    private const int BottomMargin = 48;

    // Without EndEllipsis: measured against no width, it would shorten the name to its ellipsis.
    private const TextFormatFlags MeasureFlags = TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;

    private const TextFormatFlags TextFlags =
        TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;

    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int CS_DROPSHADOW = 0x00020000;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, in int value, int size);

    private readonly System.Windows.Forms.Timer _hideTimer;
    private string _text = string.Empty;

    public SoundOutputToast()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Theme.Surface;
        DoubleBuffered = true;
        AccessibleRole = AccessibleRole.Alert;

        _hideTimer = new System.Windows.Forms.Timer { Interval = (int)ShownFor.TotalMilliseconds };
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            Hide();
        };
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            cp.ClassStyle |= CS_DROPSHADOW;
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // Windows 10 has no rounded corners and returns an error, leaving square ones.
        _ = DwmSetWindowAttribute(Handle, DWMWA_WINDOW_CORNER_PREFERENCE, DWMWCP_ROUND, sizeof(int));
    }

    // Shows the text at the bottom of the cursor's screen, restarting the countdown if already shown.
    public void ShowMessage(string text)
    {
        _text = text;
        AccessibleName = text;

        int padding = LogicalToDeviceUnits(SidePadding);
        int glyphWidth = TextRenderer.MeasureText(SpeakerGlyph, Theme.IconFont).Width;
        int textWidth = Math.Min(TextRenderer.MeasureText(text, Theme.UiFontBold, Size.Empty, MeasureFlags).Width, LogicalToDeviceUnits(MaxTextWidth));
        var size = new Size(padding + glyphWidth + LogicalToDeviceUnits(GlyphGap) + textWidth + padding, LogicalToDeviceUnits(ToastHeight));

        var work = Screen.FromPoint(Cursor.Position).WorkingArea;
        Bounds = new Rectangle(
            work.X + (work.Width - size.Width) / 2,
            work.Bottom - LogicalToDeviceUnits(BottomMargin) - size.Height,
            size.Width,
            size.Height);

        Invalidate();
        if (!Visible)
            Show();
        _hideTimer.Stop();
        _hideTimer.Start();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using (var pen = new Pen(Theme.Line))
            e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);

        int padding = LogicalToDeviceUnits(SidePadding);
        int glyphWidth = TextRenderer.MeasureText(SpeakerGlyph, Theme.IconFont).Width;
        var glyphBounds = new Rectangle(padding, 0, glyphWidth, Height);
        TextRenderer.DrawText(e.Graphics, SpeakerGlyph, Theme.IconFont, glyphBounds, Theme.Accent, TextFlags);

        int textX = glyphBounds.Right + LogicalToDeviceUnits(GlyphGap);
        var textBounds = new Rectangle(textX, 0, Width - padding - textX, Height);
        TextRenderer.DrawText(e.Graphics, _text, Theme.UiFontBold, textBounds, Theme.Ink, TextFlags);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _hideTimer.Dispose();
        base.Dispose(disposing);
    }
}
