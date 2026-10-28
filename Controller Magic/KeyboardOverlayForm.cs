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
        private readonly OverlayRepaintGate _repaintGate = new();
        private readonly KeyEchoForm _keyEcho;
        private const string Legend = "X = ⌫   Y = ␣   B = .";
        // Keyed by the DPI it was measured at, which changes when the window moves monitors.
        private (float Dpi, SizeF Size)? _legendSize;

        // One string per tile, built once instead of per tile per frame.
        internal static readonly string[,,] TileLabels = BuildTileLabels();

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
            // Owned, so it always sits above the wheel.
            _keyEcho = new KeyEchoForm(clock) { Owner = this };

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
                RequestRepaintIfChanged();
            };
        }

        // What the next paint draws, fixed when it is requested: the poll thread can move on before
        // WM_PAINT arrives, and a partial repaint of a newer frame would leave the rest stale.
        private OverlayFrame _drawFrame;

        private void RequestRepaintIfChanged()
        {
            var frame = OverlayFrame.Of(_poller);
            if (!_repaintGate.NeedsRepaint(frame))
                return;

            var dirty = _repaintGate.DirtyAreas(frame, ClientSize.Width / 2f, ClientSize.Height / 2f);
            _drawFrame = frame;
            if (dirty is null)
            {
                Invalidate();
                return;
            }

            if (dirty.Count == 0)
                _repaintGate.Painted(frame);

            foreach (var area in dirty)
                Invalidate(area);
        }

        private void InvalidateAll()
        {
            _drawFrame = OverlayFrame.Of(_poller);
            Invalidate();
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
            InvalidateAll();
            Update();
            BeginInvoke(() => Opacity = 1);
        }

        public void HideKeyboard()
        {
            _timer.Stop();
            _keyEcho.Stop();
            Hide();
        }

        // Shows the typed letter in the empty middle of the wheel, fading out.
        public void EchoKey(char letter)
        {
            if (Visible)
                _keyEcho.Echo(letter, new Point(Left + Width / 2, Top + Height / 2));
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
            InvalidateAll();
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
            InvalidateAll();
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

            var frame = _drawFrame;
            _repaintGate.Painted(frame);
            if (!frame.KeyboardMode)
                return;

            _keyboardPaintCount++;
            var layout = ControllerPoller.KeyboardLayout;
            int layer = frame.Layer;
            int hot = frame.Sector;
            int slot = frame.Slot;

            float cx = ClientSize.Width / 2f;
            float cy = ClientSize.Height / 2f;

            // Tiles outside the invalidated area are already on screen.
            var dirty = e.ClipRectangle;

            for (int sector = 0; sector < 8; sector++)
            {
                bool isHot = (sector == hot);

                for (int index = 0; index < 4; index++)
                {

                    var entry = layout[layer, sector, index];
                    if (entry.Vk == 0)
                        continue;
                    bool isSelectedSlot = (index == slot);

                    var rect = WheelTiles.Bounds(sector, index, cx, cy);
                    if (!dirty.IntersectsWith(Rectangle.Ceiling(RectangleF.Inflate(rect, WheelTiles.Margin, WheelTiles.Margin))))
                        continue;

                    g.FillEllipse(isHot && isSelectedSlot ? _hotBrush : _normalBrush, rect);
                    g.DrawEllipse(_pen, rect);

                    g.DrawString(TileLabels[layer, sector, index], _tileFont, _textBrush, rect, _centerFormat);
                }
            }

            DrawLegend(g);
        }

        private void DrawLegend(Graphics g)
        {
            if (_legendSize is not { } cached || cached.Dpi != g.DpiY)
                _legendSize = cached = (g.DpiY, g.MeasureString(Legend, _legendFont));

            float x = ClientSize.Width - cached.Size.Width - 20;
            float y = ClientSize.Height - cached.Size.Height - 20;

            g.DrawString(Legend, _legendFont, _textBrush, x, y);
        }

        private static string[,,] BuildTileLabels()
        {
            var layout = ControllerPoller.KeyboardLayout;
            var labels = new string[layout.GetLength(0), layout.GetLength(1), layout.GetLength(2)];
            for (int layer = 0; layer < labels.GetLength(0); layer++)
                for (int sector = 0; sector < labels.GetLength(1); sector++)
                    for (int index = 0; index < labels.GetLength(2); index++)
                        labels[layer, sector, index] = layout[layer, sector, index].Display.ToString();
            return labels;
        }

        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_NOACTIVATE = 0x08000000;
    }
}
