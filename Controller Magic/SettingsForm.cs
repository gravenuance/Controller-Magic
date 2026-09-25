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

            BeginCard("Guide button");
            AddHidHideToggle();
            EndCard();

            BeginCard("Mouse movement");
            AddSlider(
                name: "Deadzone",
                range: AppSettings.StickDeadZoneRange,
                get: () => AppSettings.Instance.StickDeadZone,
                set: v => AppSettings.Instance.StickDeadZone = v,
                formatReadout: v => $"{v} / 32767",
                gaugeFraction: v => v / (double)AppSettings.StickDeadZoneRange.Max);
            AddSlider(
                name: "Sensitivity",
                range: AppSettings.StickSensitivityRange,
                get: () => AppSettings.StickSensitivityRange.ToSlider(AppSettings.Instance.StickSensitivity),
                set: v => AppSettings.Instance.StickSensitivity = AppSettings.StickSensitivityRange.FromSlider(v),
                formatReadout: v => (v / 1000f).ToString("0.000"));
            AddSlider(
                name: "Acceleration curve",
                range: AppSettings.StickAccelPowerRange,
                get: () => AppSettings.StickAccelPowerRange.ToSlider(AppSettings.Instance.StickAccelPower),
                set: v => AppSettings.Instance.StickAccelPower = AppSettings.StickAccelPowerRange.FromSlider(v),
                formatReadout: v => (v / 10f).ToString("0.0"),
                curveFn: (v, t) => Math.Pow(t, v / 10.0));
            AddSlider(
                name: "Speed ramp-up",
                range: AppSettings.StickRampSecondsRange,
                get: () => AppSettings.StickRampSecondsRange.ToSlider(AppSettings.Instance.StickRampSeconds),
                set: v => AppSettings.Instance.StickRampSeconds = AppSettings.StickRampSecondsRange.FromSlider(v),
                formatReadout: v => (v / 100f).ToString("0.00") + "s",
                curveFn: (v, t) =>
                {
                    double seconds = v / 100.0;
                    return ControllerPoller.ComputeHoldRamp(t * seconds, seconds);
                });
            AddSlider(
                name: "Touchpad speed",
                range: AppSettings.TouchpadSpeedRange,
                get: () => AppSettings.Instance.TouchpadSpeed,
                set: v => AppSettings.Instance.TouchpadSpeed = v,
                formatReadout: v => $"{v} px",
                gaugeFraction: v => (v - AppSettings.TouchpadSpeedRange.Min) /
                    (double)(AppSettings.TouchpadSpeedRange.Max - AppSettings.TouchpadSpeedRange.Min));
            EndCard();

            BeginCard("Other deadzones");
            AddSlider(
                name: "Scroll",
                range: AppSettings.ScrollDeadZoneRange,
                get: () => AppSettings.Instance.ScrollDeadZone,
                set: v => AppSettings.Instance.ScrollDeadZone = v,
                formatReadout: v => v.ToString());
            AddSlider(
                name: "Keyboard",
                range: AppSettings.KeyboardDeadZoneRange,
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
                _card.AccessibleName = title;
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
            string name, SettingRange range,
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

            _cardY += 20;

            var slider = new Slider
            {
                Minimum = range.Min,
                Maximum = range.Max,
                Location = new Point(CardPadding, _cardY + 3),
                Size = new Size(sliderWidth, 20),
                TabIndex = _nextTabIndex++,
                AccessibleName = name,
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
                Location = new Point(CardPadding, _cardY + 3),
                Font = Theme.UiFontBold,
                ForeColor = Theme.Ink,
            };

            // AppSettings' last-known value shows immediately - it's already in memory, no I/O
            // needed - rather than blocking dialog construction on a schtasks.exe query. A
            // background reconciliation below corrects it if the real OS state disagrees (e.g. the
            // scheduled task was removed outside the app).
            var toggle = new ToggleSwitch
            {
                Checked = AppSettings.Instance.RunAtStartup,
                TabIndex = _nextTabIndex++,
                AccessibleName = "Start with Windows",
            };
            toggle.Location = new Point(_card.Width - CardPadding - toggle.Width, _cardY);

            toggle.CheckedChanged += async (_, __) =>
            {
                // Stays on the UI thread: toggle is read again after the await.
                await StartupHelper.SetEnabledAsync(toggle.Checked).ConfigureAwait(true);
                AppSettings.Instance.RunAtStartup = toggle.Checked;
                RequestSave();
            };

            _card.Controls.Add(title);
            _card.Controls.Add(toggle);

            _cardY += toggle.Height;

            _ = ReconcileStartupToggleAsync(toggle);
        }

        private static async Task ReconcileStartupToggleAsync(ToggleSwitch toggle)
        {
            bool actuallyEnabled = await StartupHelper.IsEnabledAsync().ConfigureAwait(false);

            if (toggle.IsDisposed || actuallyEnabled == toggle.Checked)
                return;

            void Apply()
            {
                if (!toggle.IsDisposed)
                    toggle.Checked = actuallyEnabled;
            }

            if (toggle.InvokeRequired)
                toggle.BeginInvoke((Action)Apply);
            else
                Apply();
        }

        private const string HidHideNeedsDriversText = "Needs drivers - connect to the internet to install.";

        private void AddHidHideToggle()
        {
            if (_card == null) throw new InvalidOperationException("AddHidHideToggle called outside a card");

            var title = new Label
            {
                Text = "Use HidHide",
                AutoSize = true,
                Location = new Point(CardPadding, _cardY + 3),
                Font = Theme.UiFontBold,
                ForeColor = Theme.Ink,
            };

            // Starts disabled/unchecked-looking until the background driver probe below decides
            // whether it can be turned on at all - unlike the startup toggle, there's no cheap
            // last-known value to show immediately (installing a driver isn't a fire-and-forget
            // background correction the way a scheduled task toggle is).
            var toggle = new ToggleSwitch
            {
                Checked = AppSettings.Instance.UseHidHide,
                Enabled = false,
                TabIndex = _nextTabIndex++,
                AccessibleName = "Use HidHide",
            };
            toggle.Location = new Point(_card.Width - CardPadding - toggle.Width, _cardY);

            _card.Controls.Add(title);
            _card.Controls.Add(toggle);
            _cardY += toggle.Height + 2;

            var status = new Label
            {
                Text = "Checking...",
                AutoSize = true,
                Location = new Point(CardPadding, _cardY),
                Font = Theme.CaptionFont,
                ForeColor = Theme.Muted,
                MaximumSize = new Size(_card.Width - CardPadding * 2, 0),
            };
            _card.Controls.Add(status);
            _cardY += 18;

            // Detached while handling one change and reattached afterward: the failure path below
            // reverts toggle.Checked itself, which would otherwise re-enter this same handler
            // (ToggleSwitch.CheckedChanged fires on any actual value change) and immediately
            // overwrite the failure message this handler is about to show.
            EventHandler? handler = null;
            handler = async (_, __) =>
            {
                toggle.CheckedChanged -= handler;
                try
                {
                    await OnHidHideToggleChangedAsync(toggle, status).ConfigureAwait(true);
                }
                finally
                {
                    toggle.CheckedChanged += handler;
                }
            };
            toggle.CheckedChanged += handler;

            _ = ReconcileHidHideToggleAsync(toggle, status);
        }

        private async Task OnHidHideToggleChangedAsync(ToggleSwitch toggle, Label status)
        {
            if (!toggle.Checked)
            {
                AppSettings.Instance.UseHidHide = false;
                RequestSave();
                status.Text = string.Empty;
                return;
            }

            var driverStatus = await _poller.DetectDriverStatusAsync().ConfigureAwait(true);
            if (!driverStatus.HidHideInstalled || !driverStatus.VigemInstalled)
            {
                var progress = new Progress<string>(text => status.Text = text);
                var result = await DriverInstaller.InstallAsync(driverStatus, progress, CancellationToken.None).ConfigureAwait(true);

                switch (result.Outcome)
                {
                    case InstallOutcome.Success:
                        await _poller.RefreshDriverStatusAsync().ConfigureAwait(true);
                        break;

                    case InstallOutcome.RebootRequired:
                        // Intent is still "on" - a driver install genuinely needs a restart to
                        // finish, so save that now rather than making the user flip the toggle
                        // again after rebooting. GamepadPassthroughController's own driver check
                        // won't actually activate anything until it detects the driver is truly
                        // operational, so this can't turn cloaking on prematurely.
                        await _poller.RefreshDriverStatusAsync().ConfigureAwait(true);
                        AppSettings.Instance.UseHidHide = true;
                        RequestSave();
                        status.Text = "Restart your computer to finish setup.";
                        return;

                    default:
                        toggle.Checked = false;
                        status.Text = result.Outcome switch
                        {
                            InstallOutcome.ElevationDeclined => "Elevation was cancelled.",
                            InstallOutcome.NetworkError => "No network - couldn't download drivers.",
                            InstallOutcome.DiskError => "Couldn't save drivers - check disk space.",
                            InstallOutcome.SignatureVerificationFailed => "Driver signature check failed.",
                            _ => "Driver install failed - see log.",
                        };
                        return;
                }
            }

            AppSettings.Instance.UseHidHide = true;
            RequestSave();
            status.Text = string.Empty;
        }

        private async Task ReconcileHidHideToggleAsync(ToggleSwitch toggle, Label status)
        {
            var driverStatus = await _poller.DetectDriverStatusAsync().ConfigureAwait(false);
            bool enabled = DriverDependency.ShouldToggleBeEnabled(driverStatus);

            if (toggle.IsDisposed)
                return;

            void Apply()
            {
                if (toggle.IsDisposed || status.IsDisposed)
                    return;

                toggle.Enabled = enabled;
                status.Text = enabled ? string.Empty : HidHideNeedsDriversText;
            }

            if (toggle.InvokeRequired)
                toggle.BeginInvoke((Action)Apply);
            else
                Apply();
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
                AccessibleName = label,
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

        private static void RequestSave() => AppSettings.Instance.RequestSave();

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _statusTimer.Stop();
            AppSettings.Instance.FlushPendingSave();
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
