using System.Drawing.Drawing2D;

namespace ControllerMagic
{
    // A real pill-shaped on/off switch, owner-drawn since WinForms has no native equivalent -
    // used in place of a button-styled CheckBox so "on" reads unambiguously at a glance instead
    // of looking like any other button in the window.
    internal sealed class ToggleSwitch : Control
    {
        // Setting this in code never raises Toggled, so a background status check can't act as a user click.
        public bool Checked
        {
            get;
            set
            {
                if (field == value) return;
                field = value;
                Invalidate();
                AccessibilityNotifyClients(AccessibleEvents.StateChange, -1);
            }
        }

        // While true the switch keeps focus but ignores clicks and keys, e.g. while a change it started is applied.
        public bool Busy
        {
            get;
            set
            {
                if (field == value) return;
                field = value;
                UpdateCursor();
                Invalidate();
                AccessibilityNotifyClients(AccessibleEvents.StateChange, -1);
            }
        }

        // Raised only when the user flips the switch.
        public event EventHandler? Toggled;

        public Color TrackOffColor { get; set; } = Color.FromArgb(0x22, 0x26, 0x2D);
        public Color TrackOnColor { get; set; } = Color.FromArgb(0xE8, 0xA3, 0x3D);
        public Color BorderColor { get; set; } = Color.FromArgb(0x2C, 0x30, 0x38);
        public Color KnobColor { get; set; } = Color.FromArgb(0xF3, 0xF0, 0xEA);

        public ToggleSwitch()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            Size = new Size(40, 22);
            Cursor = Cursors.Hand;
            TabStop = true;
        }

        internal void ToggleByUser()
        {
            if (Busy) return;
            Checked = !Checked;
            Toggled?.Invoke(this, EventArgs.Empty);
        }

        protected override void OnClick(EventArgs e)
        {
            ToggleByUser();
            base.OnClick(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter)
            {
                ToggleByUser();
                e.Handled = true;
            }
            base.OnKeyDown(e);
        }

        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override bool IsInputKey(Keys keyData) => keyData is Keys.Space or Keys.Enter || base.IsInputKey(keyData);

        // UserPaint controls don't repaint themselves on an Enabled change by default, and a
        // disabled control showing a hand cursor reads as a bug.
        protected override void OnEnabledChanged(EventArgs e)
        {
            UpdateCursor();
            Invalidate();
            base.OnEnabledChanged(e);
        }

        private void UpdateCursor() => Cursor = Enabled && !Busy ? Cursors.Hand : Cursors.Default;

        private static Color Muted(Color c) => Blend(c, Theme.Muted, 0.6f);

        private static Color Blend(Color a, Color b, float t) => Color.FromArgb(
            (int)(a.R + ((b.R - a.R) * t)),
            (int)(a.G + ((b.G - a.G) * t)),
            (int)(a.B + ((b.B - a.B) * t)));

        // AccessibleName is set per-instance by whoever places this switch; this reports the
        // on/off state through it, the same contract a native CheckBox gives a screen reader.
        protected override AccessibleObject CreateAccessibilityInstance() => new ToggleAccessibleObject(this);

        private sealed class ToggleAccessibleObject(ToggleSwitch owner) : ControlAccessibleObject(owner)
        {
            public override AccessibleRole Role => AccessibleRole.CheckButton;
            public override AccessibleStates State =>
                base.State
                | (owner.Checked ? AccessibleStates.Checked : AccessibleStates.None)
                | (owner.Busy ? AccessibleStates.Busy : AccessibleStates.None)
                | (owner.Focused ? AccessibleStates.Focused : AccessibleStates.None);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using var path = RoundedRect(rect, Height / 2f);

            Color trackColor = Checked ? TrackOnColor : TrackOffColor;
            Color borderColor = Checked ? TrackOnColor : BorderColor;
            Color knobColor = KnobColor;
            if (!Enabled || Busy)
            {
                trackColor = Muted(trackColor);
                borderColor = Muted(borderColor);
                knobColor = Muted(knobColor);
            }

            using (var fill = new SolidBrush(trackColor))
                g.FillPath(fill, path);
            using (var pen = new Pen(borderColor))
                g.DrawPath(pen, path);

            float knobD = Height - 6;
            float knobX = Checked ? Width - knobD - 3 : 3;
            using (var knobBrush = new SolidBrush(knobColor))
                g.FillEllipse(knobBrush, knobX, 3, knobD, knobD);

            if (Focused)
            {
                using var focusPen = new Pen(Color.FromArgb(160, TrackOnColor), 1.5f) { DashStyle = DashStyle.Dot };
                g.DrawPath(focusPen, path);
            }
        }

        internal static GraphicsPath RoundedRect(Rectangle r, float radius)
        {
            var path = new GraphicsPath();
            float d = radius * 2;
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
