using System.Drawing.Drawing2D;

namespace ControllerMagic
{
    // A real pill-shaped on/off switch, owner-drawn since WinForms has no native equivalent -
    // used in place of a button-styled CheckBox so "on" reads unambiguously at a glance instead
    // of looking like any other button in the window.
    internal sealed class ToggleSwitch : Control
    {
        private bool _checked;
        public bool Checked
        {
            get => _checked;
            set
            {
                if (_checked == value) return;
                _checked = value;
                Invalidate();
                CheckedChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public event EventHandler? CheckedChanged;

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

        protected override void OnClick(EventArgs e)
        {
            Checked = !Checked;
            base.OnClick(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter)
            {
                Checked = !Checked;
                e.Handled = true;
            }
            base.OnKeyDown(e);
        }

        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override bool IsInputKey(Keys keyData) => keyData is Keys.Space or Keys.Enter || base.IsInputKey(keyData);

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using var path = RoundedRect(rect, Height / 2f);

            using (var fill = new SolidBrush(Checked ? TrackOnColor : TrackOffColor))
                g.FillPath(fill, path);
            using (var pen = new Pen(Checked ? TrackOnColor : BorderColor))
                g.DrawPath(pen, path);

            float knobD = Height - 6;
            float knobX = Checked ? Width - knobD - 3 : 3;
            using (var knobBrush = new SolidBrush(KnobColor))
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
