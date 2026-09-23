namespace ControllerMagic
{
    internal sealed partial class KeyboardOverlayForm : Form
    {

        private static readonly TimeSpan VerifyDelay = TimeSpan.FromMilliseconds(150);
        private static readonly TimeSpan TimerStallThreshold = TimeSpan.FromMilliseconds(500);
        private static readonly TimeSpan UiStallThreshold = TimeSpan.FromMilliseconds(250);

        private readonly ControllerPoller _poller;
        private readonly TimeProvider _clock;
        private readonly System.Windows.Forms.Timer _timer;
        private int _keyboardPaintCount;
        private long _lastTimerTickTimestamp;

        // BackColor/TransparencyKey below must stay pure black - that exact color is the chroma
        // key that makes the rest of the window invisible, so only these drawn tiles show up
        // floating over the desktop. Everything drawn onto it follows the amber instrument-panel
        // palette used everywhere else now, in place of the old neon lime.
        private readonly SolidBrush _textBrush = new(Theme.Ink);
        private readonly SolidBrush _hotBrush = new(Color.FromArgb(220, Theme.Accent));
        private readonly SolidBrush _normalBrush = new(Color.FromArgb(190, Theme.Surface));
        private readonly Pen _pen = new(Theme.Accent, 1.5f);
        private readonly Font _tileFont = new("Segoe UI", 16f, FontStyle.Bold);
        private readonly Font _legendFont = new("Segoe UI", 12f, FontStyle.Regular);
        private readonly StringFormat _centerFormat = new()
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };

        public KeyboardOverlayForm(ControllerPoller poller, TimeProvider clock)
        {
            InitializeComponent();

            _poller = poller;
            _clock = clock;
            _lastTimerTickTimestamp = clock.GetTimestamp();

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;

            BackColor = Color.Black;
            TransparencyKey = Color.Black;

            StartPosition = FormStartPosition.Manual;
            Size = new Size(500, 500);

            DoubleBuffered = true;

            // Runs only while the keyboard is shown, for the live sector highlight.
            _timer = new System.Windows.Forms.Timer { Interval = 16 }; // ~60 FPS
            _timer.Tick += (_, __) =>
            {
                _lastTimerTickTimestamp = _clock.GetTimestamp();
                Invalidate();
            };
        }

        // The overlay must never take focus - keys it types have to land in the app underneath.
        protected override bool ShowWithoutActivation => true;

        // Each open is a fresh show, which also puts the window back on top of every other
        // always-on-top window. Opacity 0 until the next message-loop pass hides DWM's white
        // placeholder frame for a newly shown window (confirmed by screen capture on 2026-09-05)
        // and any stale frame from the previous open.
        public void ShowKeyboard()
        {
            Opacity = 0;
            if (!Visible)
                Show();
            OverlayWindowProbe.BringToTopmost(Handle);

            _lastTimerTickTimestamp = _clock.GetTimestamp();
            _timer.Start();
            Invalidate();
            Update();
            BeginInvoke(() => Opacity = 1);
        }

        public void HideKeyboard()
        {
            _timer.Stop();
            Hide();
        }

        // Called as keyboard mode opens: lets a few frames render, then checks the window can really
        // be seen, and repairs and logs it only when it can't.
        public async Task VerifyVisibleAsync(TimeSpan uiDelay)
        {
            try
            {
                if (uiDelay > UiStallThreshold)
                    AppLog.Default.Warning($"KeyboardOverlay: UI thread took {uiDelay.TotalMilliseconds:F0}ms to react to keyboard mode");

                var problems = await CheckAfterFramesAsync().ConfigureAwait(true);
                if (problems is null or OverlayProblem.None)
                    return;

                AppLog.Default.Warning($"KeyboardOverlay: not visible ({problems}); recovering");
                Recover(problems.Value);

                var remaining = await CheckAfterFramesAsync().ConfigureAwait(true);
                if (remaining is null)
                    return;
                if (remaining == OverlayProblem.None)
                    AppLog.Default.Info("KeyboardOverlay: recovered");
                else
                    AppLog.Default.Warning($"KeyboardOverlay: still not visible after recovery ({remaining})");
            }
            catch (ObjectDisposedException)
            {
                // The app exited while waiting; nothing left to check.
            }
        }

        // Null when keyboard mode closed during the wait, since there's nothing left to judge.
        private async Task<OverlayProblem?> CheckAfterFramesAsync()
        {
            int paintsBefore = _keyboardPaintCount;
            Invalidate();
            await Task.Delay(VerifyDelay, _clock).ConfigureAwait(true);
            if (!_poller.KeyboardMode)
                return null;

            bool timerTicking = _clock.GetElapsedTime(_lastTimerTickTimestamp) < TimerStallThreshold;
            var snapshot = OverlayWindowProbe.Capture(this, painted: _keyboardPaintCount != paintsBefore, timerTicking);
            var problems = OverlayHealth.Evaluate(snapshot);
            if (problems != OverlayProblem.None)
                AppLog.Default.Info($"KeyboardOverlay: {snapshot}, bounds {Bounds}");
            return problems;
        }

        private void Recover(OverlayProblem problems)
        {
            if (problems.HasFlag(OverlayProblem.TimerStalled))
            {
                _timer.Stop();
                _timer.Start();
            }

            if (problems.HasFlag(OverlayProblem.Transparent))
                Opacity = 1;

            // Re-showing makes the shell re-evaluate a window it hid or cloaked behind our back.
            if (problems.HasFlag(OverlayProblem.Hidden) || problems.HasFlag(OverlayProblem.Cloaked))
            {
                Hide();
                Show();
            }

            OverlayWindowProbe.BringToTopmost(Handle);
            Invalidate();
            Update();
        }

        // Click-through and never activated - independent of the layered/opacity machinery, so
        // baking it into CreateParams from the start doesn't disturb the colour-key setup.
        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= WS_EX_TRANSPARENT | WS_EX_NOACTIVATE;
                return cp;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            if (!_poller.KeyboardMode)
                return;

            _keyboardPaintCount++;
            var layout = ControllerPoller.KeyboardLayout;
            int layer = _poller.KeyboardLayer;
            int hot = _poller.CurrentSector;
            int slot = _poller.SlotIndex;

            float cx = ClientSize.Width / 2f;
            float cy = ClientSize.Height / 2f;
            float baseRadius = 120f; // a bit farther out

            float tileSize = 40f;

            for (int sector = 0; sector < 8; sector++)
            {
                bool isHot = (sector == hot);

                double angleDeg = 90.0 + sector * 45.0;

                double angleRad = angleDeg * Math.PI / 180.0;

                for (int index = 0; index < 4; index++)
                {

                    var entry = layout[layer, sector, index];
                    if (entry.Vk == 0)
                        continue;
                    bool isSelectedSlot = (index == slot);
                    float inner = baseRadius + index * (tileSize + 4f);
                    float outer = inner + tileSize;
                    float midR = (inner + outer) / 2f;

                    float x = cx + (float)(midR * Math.Cos(angleRad));
                    float y = cy - (float)(midR * Math.Sin(angleRad));

                    var rect = new RectangleF(
                        x - tileSize / 2f,
                        y - tileSize / 2f,
                        tileSize,
                        tileSize
                    );

                    g.FillEllipse(isHot && isSelectedSlot ? _hotBrush : _normalBrush, rect);
                    g.DrawEllipse(_pen, rect);

                    g.DrawString(entry.Display.ToString(), _tileFont, _textBrush, rect, _centerFormat);
                }
            }

            DrawLegend(g);
        }

        private void DrawLegend(Graphics g)
        {
            const string legend = "X = ⌫   Y = ␣   B = .";

            var size = g.MeasureString(legend, _legendFont);
            float x = ClientSize.Width - size.Width - 20;
            float y = ClientSize.Height - size.Height - 20;

            g.DrawString(legend, _legendFont, _textBrush, x, y);
        }

        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_NOACTIVATE = 0x08000000;
    }
}
