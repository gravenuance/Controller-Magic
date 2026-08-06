namespace ControllerMagic
{
    public partial class SettingsForm : Form
    {
        // Bundles a labeled TrackBar with the get/set/format glue needed to keep it in sync with
        // AppSettings, so adding another slider is one AddSlider(...) call instead of a new
        // Designer.cs block plus a matching hand-written Scroll handler.
        private sealed class SliderSetting
        {
            public required Label Label { get; init; }
            public required TrackBar TrackBar { get; init; }
            public required Func<int> Get { get; init; }
            public required Action<int> Set { get; init; }
            public required Func<int, string> FormatLabel { get; init; }

            public void RefreshFromSettings()
            {
                TrackBar.Value = Math.Clamp(Get(), TrackBar.Minimum, TrackBar.Maximum);
                Label.Text = FormatLabel(TrackBar.Value);
            }

            public void Commit()
            {
                Set(TrackBar.Value);
                AppSettings.Instance.Save();
                Label.Text = FormatLabel(TrackBar.Value);
            }
        }

        private const int LeftMargin = 20;
        private const int ContentWidth = 430;

        private int _layoutY = 20;
        private int _nextTabIndex;

        public SettingsForm()
        {
            InitializeComponent();
            Icon = ControllerMagic.Properties.Resources.Controller;
            BuildLayout();
        }

        private void BuildLayout()
        {
            AddStartupToggle();

            AddSectionHeader("Mouse movement");
            AddSlider(
                min: 0, max: 10000, tickFrequency: 1000,
                get: () => AppSettings.Instance.StickDeadZone,
                set: v => AppSettings.Instance.StickDeadZone = v,
                format: v => $"Stick deadzone (move): {v}");
            AddSlider(
                min: 5, max: 60, tickFrequency: 5,
                get: () => (int)Math.Round(AppSettings.Instance.StickSensitivity * 1000f),
                set: v => AppSettings.Instance.StickSensitivity = v / 1000f,
                format: FormatSensitivityLabel);
            AddSlider(
                min: 10, max: 40, tickFrequency: 5,
                get: () => (int)Math.Round(AppSettings.Instance.StickAccelPower * 10f),
                set: v => AppSettings.Instance.StickAccelPower = v / 10f,
                format: FormatAccelPowerLabel);

            AddSectionHeader("Other deadzones");
            AddSlider(
                min: 0, max: 10000, tickFrequency: 1000,
                get: () => AppSettings.Instance.ScrollDeadZone,
                set: v => AppSettings.Instance.ScrollDeadZone = v,
                format: v => $"Stick deadzone (scroll): {v}");
            AddSlider(
                min: 0, max: 10000, tickFrequency: 1000,
                get: () => AppSettings.Instance.KeyboardDeadZone,
                set: v => AppSettings.Instance.KeyboardDeadZone = v,
                format: v => $"Stick deadzone (keyboard): {v}");

            AddSectionHeader("Full-screen apps");
            AddFullscreenAppsRow();

            ClientSize = new Size(ClientSize.Width, _layoutY + LeftMargin);
        }

        private static string FormatSensitivityLabel(int value)
        {
            float f = value / 1000f;
            string note =
                f < 0.012f ? "slow" :
                f < 0.028f ? "balanced" :
                f < 0.040f ? "fast" : "very fast";
            return $"Stick sensitivity ({f:0.000} - {note})";
        }

        private static string FormatAccelPowerLabel(int value)
        {
            float f = value / 10f;
            string note =
                f < 1.5f ? "gentle ramp" :
                f < 2.5f ? "balanced" :
                f < 3.5f ? "aggressive" : "very aggressive";
            return $"Stick acceleration curve ({f:0.0} - {note})";
        }

        // Draws a thin divider (skipped above the very first section) plus a small caption, so
        // related sliders read as a group instead of one undifferentiated stack.
        private void AddSectionHeader(string text)
        {
            if (_layoutY > LeftMargin)
            {
                Controls.Add(new Panel
                {
                    BackColor = Color.FromArgb(60, 60, 60),
                    Location = new Point(LeftMargin, _layoutY),
                    Size = new Size(ContentWidth, 1)
                });
                _layoutY += 14;
            }

            Controls.Add(new Label
            {
                AutoSize = true,
                ForeColor = Color.FromArgb(90, 200, 90),
                Font = new Font("Segoe UI", 8f, FontStyle.Bold),
                Location = new Point(LeftMargin, _layoutY),
                Text = text.ToUpperInvariant()
            });
            _layoutY += 24;
        }

        private void AddSlider(int min, int max, int tickFrequency, Func<int> get, Action<int> set, Func<int, string> format)
        {
            var label = new Label
            {
                AutoSize = true,
                ForeColor = Color.Lime,
                Location = new Point(LeftMargin, _layoutY)
            };
            _layoutY += 20;

            var trackBar = new TrackBar
            {
                Minimum = min,
                Maximum = max,
                TickFrequency = tickFrequency,
                Location = new Point(LeftMargin, _layoutY),
                Size = new Size(ContentWidth, 45),
                TabIndex = _nextTabIndex++
            };
            _layoutY += 50;

            var slider = new SliderSetting { Label = label, TrackBar = trackBar, Get = get, Set = set, FormatLabel = format };
            slider.RefreshFromSettings();
            trackBar.Scroll += (_, __) => slider.Commit();

            Controls.Add(label);
            Controls.Add(trackBar);
        }

        private void AddStartupToggle()
        {
            var checkBox = new CheckBox
            {
                AutoSize = false,
                Appearance = Appearance.Button,
                FlatStyle = FlatStyle.Flat,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.White,
                Location = new Point(LeftMargin, _layoutY),
                Size = new Size(120, 28),
                TabIndex = _nextTabIndex++
            };
            checkBox.FlatAppearance.BorderSize = 1;
            checkBox.FlatAppearance.BorderColor = Color.DimGray;

            void UpdateVisual()
            {
                checkBox.Text = checkBox.Checked ? "Startup: ON" : "Startup: OFF";
                checkBox.BackColor = checkBox.Checked
                    ? Color.FromArgb(120, 46, 204, 113)
                    : Color.FromArgb(120, 149, 165, 166);
            }

            checkBox.Checked = StartupHelper.IsEnabled();
            AppSettings.Instance.RunAtStartup = checkBox.Checked;
            UpdateVisual();

            checkBox.CheckedChanged += (_, __) =>
            {
                StartupHelper.SetEnabled(checkBox.Checked);
                AppSettings.Instance.RunAtStartup = checkBox.Checked;
                UpdateVisual();
            };

            Controls.Add(checkBox);
            _layoutY += 40;
        }

        private void AddFullscreenAppsRow()
        {
            Controls.Add(new Label
            {
                AutoSize = true,
                ForeColor = Color.Lime,
                Location = new Point(LeftMargin, _layoutY),
                Text = "Comma-separated process names"
            });
            _layoutY += 20;

            var textBox = new TextBox
            {
                BackColor = Color.FromArgb(30, 30, 30),
                ForeColor = Color.Lime,
                BorderStyle = BorderStyle.FixedSingle,
                Location = new Point(LeftMargin, _layoutY),
                Size = new Size(ContentWidth, 23),
                Text = string.Join(", ", AppSettings.Instance.WatchedProcessNames),
                TabIndex = _nextTabIndex++
            };
            textBox.Leave += (_, __) =>
            {
                var names = textBox.Text
                    .Split(',')
                    .Select(n => n.Trim())
                    .Where(n => n.Length > 0)
                    .ToList();

                AppSettings.Instance.WatchedProcessNames = names;
                AppSettings.Instance.Save();
            };

            Controls.Add(textBox);
            _layoutY += 30;
        }

        private bool _dragging;
        private Point _dragStart;

        private void SettingsForm_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _dragging = true;
                _dragStart = e.Location;
            }
        }

        private void SettingsForm_MouseMove(object sender, MouseEventArgs e)
        {
            if (_dragging)
            {
                var screenPos = PointToScreen(e.Location);
                Location = new Point(screenPos.X - _dragStart.X, screenPos.Y - _dragStart.Y);
            }
        }

        private void SettingsForm_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
                _dragging = false;
        }

        private void CloseButton_Click(object sender, EventArgs e)
        {
            Close();
        }
    }
}
