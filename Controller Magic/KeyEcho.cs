using System.Drawing.Drawing2D;

namespace ControllerMagic;

internal readonly record struct EchoFrame(double Opacity, double Scale);

// Timing of the typed-letter echo: it stays readable at first, then fades and shrinks to half size.
internal sealed class KeyEchoAnimation(TimeProvider clock)
{
    internal static readonly TimeSpan Duration = TimeSpan.FromSeconds(0.7);
    internal const double EndScale = 0.5;

    private long? _startedAt;

    public void Start() => _startedAt = clock.GetTimestamp();

    // Null once the echo has faded out, or before any letter was typed.
    public EchoFrame? Current()
    {
        if (_startedAt is not { } started)
            return null;

        double t = clock.GetElapsedTime(started) / Duration;
        if (t >= 1)
        {
            _startedAt = null;
            return null;
        }

        double easeOut = 1 - (1 - t) * (1 - t);
        return new EchoFrame(Opacity: 1 - t * t, Scale: 1 - (1 - EndScale) * easeOut);
    }
}

// The letter just typed, faded as a whole window: the keyboard overlay's colour key can't show
// partial transparency, so fading inside it would only darken the letter, not let the desktop through.
internal sealed class KeyEchoForm : Form
{
    private const int EchoSize = 200;
    private const float LetterEm = 120f;
    private const float OutlineWidth = 8f;

    // Pure black is the colour key, so the outline is the nearest colour that still shows.
    private static readonly Color OutlineColor = Color.FromArgb(1, 1, 1);

    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    private readonly KeyEchoAnimation _animation;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly FontFamily _fontFamily = new("Segoe UI");
    private readonly SolidBrush _fill = new(Theme.Accent);
    private readonly Pen _outline = new(OutlineColor, OutlineWidth) { LineJoin = LineJoin.Round };
    private readonly StringFormat _centred = new() { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
    private string _letter = string.Empty;
    private double _scale = 1;

    public KeyEchoForm(TimeProvider clock)
    {
        _animation = new KeyEchoAnimation(clock);

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.Black;
        TransparencyKey = Color.Black;
        DoubleBuffered = true;
        Size = new Size(EchoSize, EchoSize);

        // Runs only while a letter is fading.
        _timer = new System.Windows.Forms.Timer { Interval = 16 };
        _timer.Tick += (_, _) => ApplyFrame();
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            return cp;
        }
    }

    // Restarts the fade with this letter, centred on the given point.
    public void Echo(char letter, Point centre)
    {
        _letter = char.ToUpperInvariant(letter).ToString();
        Location = new Point(centre.X - Width / 2, centre.Y - Height / 2);
        _animation.Start();

        if (!Visible)
        {
            // Fully transparent until the first frame, so DWM's placeholder frame for a new window never shows.
            Opacity = 0;
            Show();
        }

        _timer.Start();
    }

    public void Stop()
    {
        _timer.Stop();
        Hide();
    }

    private void ApplyFrame()
    {
        if (_animation.Current() is not { } frame)
        {
            Stop();
            return;
        }

        Opacity = frame.Opacity;
        _scale = frame.Scale;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (_letter.Length == 0)
            return;

        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        using var path = new GraphicsPath();
        path.AddString(_letter, _fontFamily, (int)FontStyle.Bold, (float)(LetterEm * _scale), ClientRectangle, _centred);
        g.DrawPath(_outline, path);
        g.FillPath(_fill, path);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            _fontFamily.Dispose();
            _fill.Dispose();
            _outline.Dispose();
            _centred.Dispose();
        }

        base.Dispose(disposing);
    }
}
