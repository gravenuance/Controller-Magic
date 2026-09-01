namespace ControllerMagic
{
    internal sealed partial class SettingsForm : Form
    {
        // Bundles a Slider with the get/set/format/visualization glue needed to keep it in sync
        // with AppSettings, so adding another slider is one AddSlider(...) call.
        private sealed class SliderSetting
        {
            public required Slider Slider { get; init; }
            public required Label Readout { get; init; }
            public required Func<int> Get { get; init; }
            public required Action<int> Set { get; init; }
            public required Func<int, string> FormatReadout { get; init; }
            public required Action RequestSave { get; init; }
            public CurvePreview? Curve { get; init; }
            public Func<int, double, double>? CurveFn { get; init; }
            public RadialGauge? Gauge { get; init; }
            public Func<int, double>? GaugeFraction { get; init; }

            public void RefreshFromSettings()
            {
                Slider.Value = Math.Clamp(Get(), Slider.Minimum, Slider.Maximum);
                Readout.Text = FormatReadout(Slider.Value);
                UpdateViz();
            }

            public void Commit()
            {
                Set(Slider.Value);
                RequestSave();
                Readout.Text = FormatReadout(Slider.Value);
                UpdateViz();
            }

            private void UpdateViz()
            {
                if (Curve != null && CurveFn != null)
                {
                    int v = Slider.Value;
                    Curve.CurveFn = t => CurveFn(v, t);
                    Curve.Redraw();
                }
                if (Gauge != null && GaugeFraction != null)
                    Gauge.Fraction = GaugeFraction(Slider.Value);
            }
        }

        private const int OuterMargin = 16;
        private const int CardGap = 12;
        private const int CardPadding = 14;
        private const int VizWidth = 64;
        private const int VizHeight = 34;
        private const int VizGap = 10;

        private readonly ControllerPoller _poller;
        private readonly System.Windows.Forms.Timer _statusTimer;
        private readonly System.Windows.Forms.Timer _saveDebounceTimer;
        private bool _saveDirty;

        private int _layoutY;
        private int _nextTabIndex;
        private CardPanel? _card;
        private int _cardY;

        private Panel? _statusDot;
        private Label? _statusLabel;

        internal SettingsForm(ControllerPoller poller)
        {
            _poller = poller;

            InitializeComponent();
            Icon = Theme.AppIcon;

            _saveDebounceTimer = new System.Windows.Forms.Timer { Interval = 400 };
            _saveDebounceTimer.Tick += (_, __) => FlushPendingSave();

            _statusTimer = new System.Windows.Forms.Timer { Interval = 500 };
            _statusTimer.Tick += (_, __) => RefreshStatus();

            BuildLayout();

            _statusTimer.Start();
        }

        // A borderless form gets no native shadow by default; CS_DROPSHADOW gives it the same
        // soft native shadow a normal window has, instead of looking like it's pasted flat onto
        // the desktop.
        protected override CreateParams CreateParams
        {
            get
            {
                const int CS_DROPSHADOW = 0x00020000;
                var cp = base.CreateParams;
                cp.ClassStyle |= CS_DROPSHADOW;
                return cp;
            }
        }

        private void BuildLayout()
        {
            EnableDragging(this);

            BuildTitleBar();
            BuildStatusRow();

            _layoutY += 12;

            BeginCard(null);
            AddStartupToggle();
            EndCard();

            BeginCard("Mouse movement");
            AddSlider(
                name: "Deadzone", note: "Ignores drift near center",
                min: 0, max: 10000,
                get: () => AppSettings.Instance.StickDeadZone,
                set: v => AppSettings.Instance.StickDeadZone = v,
                formatReadout: v => $"{v} / 32767",
                gaugeFraction: v => v / 10000.0);
            AddSlider(
                name: "Sensitivity", note: null,
                min: 5, max: 60,
                get: () => (int)Math.Round(AppSettings.Instance.StickSensitivity * 1000f),
                set: v => AppSettings.Instance.StickSensitivity = v / 1000f,
                formatReadout: v => (v / 1000f).ToString("0.000"));
            AddSlider(
                name: "Acceleration curve", note: "Speed vs. how far you push",
                min: 10, max: 40,
                get: () => (int)Math.Round(AppSettings.Instance.StickAccelPower * 10f),
                set: v => AppSettings.Instance.StickAccelPower = v / 10f,
                formatReadout: v => (v / 10f).ToString("0.0"),
                curveFn: (v, t) => Math.Pow(t, v / 10.0));
            AddSlider(
                name: "Speed ramp-up", note: "Full speed vs. how long you hold it",
                min: 0, max: 100,
                get: () => (int)Math.Round(AppSettings.Instance.StickRampSeconds * 100f),
                set: v => AppSettings.Instance.StickRampSeconds = v / 100f,
                formatReadout: v => (v / 100f).ToString("0.00") + "s",
                curveFn: (v, t) =>
                {
                    double seconds = v / 100.0;
                    return ControllerPoller.ComputeHoldRamp(t * seconds, seconds);
                });
            EndCard();

            BeginCard("Other deadzones");
            AddSlider(
                name: "Scroll", note: null,
                min: 0, max: 10000,
                get: () => AppSettings.Instance.ScrollDeadZone,
                set: v => AppSettings.Instance.ScrollDeadZone = v,
                formatReadout: v => v.ToString());
            AddSlider(
                name: "Keyboard", note: null,
                min: 0, max: 10000,
                get: () => AppSettings.Instance.KeyboardDeadZone,
                set: v => AppSettings.Instance.KeyboardDeadZone = v,
                formatReadout: v => v.ToString());
            EndCard();

            BeginCard("Full-screen apps");
            AddChipField(
                "Keep receiving input from",
                AppSettings.Instance.WatchedProcessNames,
                items => AppSettings.Instance.WatchedProcessNames = items);
            AddChipField(
                "'S' = Skip Intro on",
                AppSettings.Instance.StreamingServiceNames,
                items => AppSettings.Instance.StreamingServiceNames = items);
            EndCard();

            ClientSize = new Size(ClientSize.Width, _layoutY + OuterMargin);
            RefreshStatus();
        }

        // ============ chrome ============

        private void BuildTitleBar()
        {
            const int barHeight = 40;
            var bar = new Panel
            {
                Location = new Point(0, 0),
                Size = new Size(ClientSize.Width, barHeight),
                BackColor = Theme.TitlebarBg,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            };
            bar.Paint += (_, e) =>
            {
                using var pen = new Pen(Theme.Line);
                e.Graphics.DrawLine(pen, 0, bar.Height - 1, bar.Width, bar.Height - 1);
            };

            var glyph = new Label
            {
                Text = "\U0001F3AE",
                AutoSize = false,
                Size = new Size(24, 24),
                Location = new Point(14, 8),
                TextAlign = ContentAlignment.MiddleCenter,
                Font = Theme.EmojiFont,
            };
            var name = new Label
            {
                Text = "Controller Magic",
                AutoSize = true,
                Location = new Point(44, 12),
                Font = Theme.UiFontBold,
                ForeColor = Theme.Ink,
            };
            var close = new Label
            {
                Text = "✕",
                AutoSize = false,
                Size = new Size(28, 28),
                Location = new Point(ClientSize.Width - 38, 6),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Theme.Muted,
                Font = new Font("Segoe UI", 9f),
                Cursor = Cursors.Hand,
                TabIndex = _nextTabIndex++,
            };
            close.Click += (_, __) => Close();
            close.MouseEnter += (_, __) => close.ForeColor = Theme.Ink;
            close.MouseLeave += (_, __) => close.ForeColor = Theme.Muted;

            // These must be children of bar, not siblings added straight to the form: WinForms
            // z-orders earlier-added siblings on top, so bar (added first, opaque) would paint
            // over glyph/name/close and hide them entirely if they weren't nested inside it.
            bar.Controls.Add(glyph);
            bar.Controls.Add(name);
            bar.Controls.Add(close);
            Controls.Add(bar);

            EnableDragging(bar);

            _layoutY = barHeight;
        }

        private void BuildStatusRow()
        {
            const int rowHeight = 34;
            var row = new Panel
            {
                Location = new Point(0, _layoutY),
                Size = new Size(ClientSize.Width, rowHeight),
                BackColor = Theme.Surface,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            };
            row.Paint += (_, e) =>
            {
                using var pen = new Pen(Theme.Line);
                e.Graphics.DrawLine(pen, 0, row.Height - 1, row.Width, row.Height - 1);
            };

            _statusDot = new Panel
            {
                Size = new Size(8, 8),
                Location = new Point(16, 13),
                BackColor = Theme.Muted,
            };
            _statusDot.Paint += (_, e) =>
            {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using var b = new SolidBrush(_statusDot.BackColor);
                e.Graphics.FillEllipse(b, 0, 0, _statusDot.Width, _statusDot.Height);
            };

            _statusLabel = new Label
            {
                Text = "Checking…",
                AutoSize = true,
                Location = new Point(31, 9),
                Font = Theme.UiFontBold,
                ForeColor = Theme.Muted,
            };

            row.Controls.Add(_statusDot);
            row.Controls.Add(_statusLabel);
            Controls.Add(row);

            EnableDragging(row);

            _layoutY += rowHeight;
        }

        private void RefreshStatus()
        {
            if (_statusLabel == null || _statusDot == null) return;

            bool connected = _poller.IsControllerConnected;
            _statusDot.BackColor = connected ? Theme.Good : Theme.Muted;
            _statusDot.Invalidate();
            _statusLabel.Text = _poller.ControllerStatusText;
            _statusLabel.ForeColor = connected ? Theme.Ink : Theme.Muted;
        }

        // ============ cards ============

        private void BeginCard(string? title)
        {
            _card = new CardPanel
            {
                Location = new Point(OuterMargin, _layoutY),
                Width = ClientSize.Width - OuterMargin * 2,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            };
            _cardY = CardPadding;

            if (title != null)
            {
                var head = new Label
                {
                    Text = title.ToUpperInvariant(),
                    AutoSize = true,
                    Location = new Point(CardPadding, _cardY),
                    Font = Theme.CaptionFont,
                    ForeColor = Theme.Muted,
                };
                _card.Controls.Add(head);
                _cardY += 22;
            }
        }

        private void EndCard()
        {
            if (_card == null) return;
            _card.Height = _cardY + CardPadding - 4;
            Controls.Add(_card);
            _layoutY += _card.Height + CardGap;
            _card = null;
        }

        // ============ controls ============

        private void AddSlider(
            string name, string? note, int min, int max,
            Func<int> get, Action<int> set, Func<int, string> formatReadout,
            Func<int, double, double>? curveFn = null,
            Func<int, double>? gaugeFraction = null)
        {
            if (_card == null) throw new InvalidOperationException("AddSlider called outside a card");

            int contentWidth = _card.Width - CardPadding * 2;
            bool hasViz = curveFn != null || gaugeFraction != null;
            int sliderWidth = hasViz ? contentWidth - VizWidth - VizGap : contentWidth;

            var nameLabel = new Label
            {
                Text = name,
                AutoSize = true,
                Location = new Point(CardPadding, _cardY),
                Font = Theme.UiFontBold,
                ForeColor = Theme.Ink,
            };
            _card.Controls.Add(nameLabel);

            var readout = new Label
            {
                AutoSize = false,
                Size = new Size(100, 16),
                Location = new Point(_card.Width - CardPadding - 100, _cardY),
                TextAlign = ContentAlignment.MiddleRight,
                Font = Theme.MonoFont,
                ForeColor = Theme.Accent,
            };
            _card.Controls.Add(readout);

            if (note != null)
            {
                var noteLabel = new Label
                {
                    Text = note,
                    AutoSize = true,
                    Location = new Point(CardPadding, _cardY + 15),
                    Font = Theme.BodyFont,
                    ForeColor = Theme.Muted,
                };
                _card.Controls.Add(noteLabel);
            }

            _cardY += note != null ? 33 : 20;

            var slider = new Slider
            {
                Minimum = min,
                Maximum = max,
                Location = new Point(CardPadding, _cardY + 3),
                Size = new Size(sliderWidth, 20),
                TabIndex = _nextTabIndex++,
            };
            _card.Controls.Add(slider);

            CurvePreview? curve = null;
            RadialGauge? gauge = null;
            var vizLocation = new Point(CardPadding + sliderWidth + VizGap, _cardY - 4);
            var vizSize = new Size(VizWidth, VizHeight);
            if (curveFn != null)
            {
                curve = new CurvePreview { Location = vizLocation, Size = vizSize };
                _card.Controls.Add(curve);
            }
            else if (gaugeFraction != null)
            {
                gauge = new RadialGauge { Location = vizLocation, Size = vizSize };
                _card.Controls.Add(gauge);
            }

            _cardY += 32;

            var setting = new SliderSetting
            {
                Slider = slider,
                Readout = readout,
                Get = get,
                Set = set,
                FormatReadout = formatReadout,
                RequestSave = RequestSave,
                Curve = curve,
                CurveFn = curveFn,
                Gauge = gauge,
                GaugeFraction = gaugeFraction,
            };
            setting.RefreshFromSettings();
            slider.Scroll += (_, __) => setting.Commit();
        }

        private void AddStartupToggle()
        {
            if (_card == null) throw new InvalidOperationException("AddStartupToggle called outside a card");

            var title = new Label
            {
                Text = "Start with Windows",
                AutoSize = true,
                Location = new Point(CardPadding, _cardY),
                Font = Theme.UiFontBold,
                ForeColor = Theme.Ink,
            };
            var sub = new Label
            {
                Text = "Launches quietly in the tray at login",
                AutoSize = true,
                Location = new Point(CardPadding, _cardY + 16),
                Font = Theme.BodyFont,
                ForeColor = Theme.Muted,
            };

            var toggle = new ToggleSwitch
            {
                Checked = StartupHelper.IsEnabled(),
                TabIndex = _nextTabIndex++,
            };
            toggle.Location = new Point(_card.Width - CardPadding - toggle.Width, _cardY + 4);
            AppSettings.Instance.RunAtStartup = toggle.Checked;

            toggle.CheckedChanged += (_, __) =>
            {
                StartupHelper.SetEnabled(toggle.Checked);
                AppSettings.Instance.RunAtStartup = toggle.Checked;
            };

            _card.Controls.Add(title);
            _card.Controls.Add(sub);
            _card.Controls.Add(toggle);

            _cardY += 34;
        }

        private void AddChipField(string label, List<string> initialItems, Action<List<string>> onChanged)
        {
            if (_card == null) throw new InvalidOperationException("AddChipField called outside a card");

            var nameLabel = new Label
            {
                Text = label,
                AutoSize = true,
                Location = new Point(CardPadding, _cardY),
                Font = Theme.UiFontBold,
                ForeColor = Theme.Ink,
            };
            _card.Controls.Add(nameLabel);
            _cardY += 20;

            var chips = new ChipList
            {
                Location = new Point(CardPadding, _cardY),
                MaximumSize = new Size(_card.Width - CardPadding * 2, 0),
                TabIndex = _nextTabIndex++,
            };
            chips.ApplyPalette(Theme.Bg, Theme.Line, Theme.Ink, Theme.Muted, Theme.Surface2);
            chips.SetItems(initialItems);
            chips.ItemsChanged += items =>
            {
                onChanged(items);
                RequestSave();
            };
            _card.Controls.Add(chips);
            chips.PerformLayout();

            _cardY += Math.Max(chips.Height, 32) + 10;
        }

        // ============ saving ============

        // Slider drags fire Scroll continuously, so saving on every tick means a synchronous file
        // write per pixel of movement. Batch changes and write once after a short idle gap instead.
        private void RequestSave()
        {
            _saveDirty = true;
            _saveDebounceTimer.Stop();
            _saveDebounceTimer.Start();
        }

        private void FlushPendingSave()
        {
            _saveDebounceTimer.Stop();
            if (!_saveDirty) return;
            _saveDirty = false;
            AppSettings.Instance.Save();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _statusTimer.Stop();
            FlushPendingSave();
            base.OnFormClosed(e);
        }

        // ============ window dragging ============

        // Cards and the titlebar/status panels now cover most of the client area, so relying on
        // the form's own MouseDown (which only fires on exposed form background, never on a child
        // control sitting on top of it) left almost nowhere left to grab. Wiring this to specific
        // surfaces instead, using screen coordinates throughout, works regardless of which control
        // the drag started on.
        private bool _dragging;
        private Point _dragMouseStart;
        private Point _dragFormStart;

        private void EnableDragging(Control surface)
        {
            surface.MouseDown += (_, e) =>
            {
                if (e.Button != MouseButtons.Left) return;
                _dragging = true;
                _dragMouseStart = Cursor.Position;
                _dragFormStart = Location;
            };
            surface.MouseMove += (_, __) =>
            {
                if (!_dragging) return;
                var cursor = Cursor.Position;
                Location = new Point(
                    _dragFormStart.X + (cursor.X - _dragMouseStart.X),
                    _dragFormStart.Y + (cursor.Y - _dragMouseStart.Y));
            };
            surface.MouseUp += (_, e) =>
            {
                if (e.Button == MouseButtons.Left)
                    _dragging = false;
            };
        }
    }
}
