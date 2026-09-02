using System.Drawing.Drawing2D;

namespace ControllerMagic
{
    // A minimal custom slider replacing the native TrackBar, which always renders with Windows'
    // chunky common-control chrome and tick marks no matter what colors are applied - there's no
    // way to make a native TrackBar read as part of a flat instrument-panel design.
    internal sealed class Slider : Control
    {
        public int Minimum { get; set; }
        public int Maximum { get; set; } = 100;

        public int Value
        {
            get;
            set
            {
                int clamped = Math.Clamp(value, Minimum, Maximum);
                if (field == clamped) return;
                field = clamped;
                Invalidate();
            }
        }

        // Fires only on user-driven change (mouse drag/click, arrow keys) - mirrors TrackBar.Scroll,
        // so programmatic Value assignment during initial load doesn't trigger a save.
        public event EventHandler? Scroll;

        public Color TrackColor { get; set; } = Theme.Surface2;
        public Color TrackBorder { get; set; } = Theme.Line;
        public Color FillColor { get; set; } = Theme.Accent;
        public Color ThumbOutline { get; set; } = Theme.Bg;

        public Slider()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable, true);
            Height = 20;
            TabStop = true;
            Cursor = Cursors.Hand;
        }

        // AccessibleName is set per-instance by whoever places this Slider (its on-screen label
        // isn't known to the control itself); this reports the live numeric value/range through
        // it, same contract a native TrackBar gives a screen reader.
        protected override AccessibleObject CreateAccessibilityInstance() => new SliderAccessibleObject(this);

        private sealed class SliderAccessibleObject(Slider owner) : ControlAccessibleObject(owner)
        {
            public override AccessibleRole Role => AccessibleRole.Slider;
            public override string? Value => owner.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            public override AccessibleStates State =>
                base.State | (owner.Focused ? AccessibleStates.Focused : AccessibleStates.None);
        }

        private const int ThumbRadius = 7;

        private void SetFromMouseX(int x)
        {
            float usable = Width - ThumbRadius * 2f;
            float t = usable <= 0 ? 0 : Math.Clamp((x - ThumbRadius) / usable, 0f, 1f);
            int newValue = Minimum + (int)Math.Round(t * (Maximum - Minimum));
            if (newValue != Value)
            {
                Value = newValue;
                Scroll?.Invoke(this, EventArgs.Empty);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            SetFromMouseX(e.X);
            Capture = true;
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
                SetFromMouseX(e.X);
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            Capture = false;
            base.OnMouseUp(e);
        }

        protected override bool IsInputKey(Keys keyData) =>
            keyData is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(keyData);

        protected override void OnKeyDown(KeyEventArgs e)
        {
            int step = Math.Max(1, (Maximum - Minimum) / 100);
            int delta = e.KeyCode switch
            {
                Keys.Left or Keys.Down => -step,
                Keys.Right or Keys.Up => step,
                _ => 0
            };
            if (delta != 0)
            {
                Value += delta;
                Scroll?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
            }
            base.OnKeyDown(e);
        }

        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            float usable = Width - ThumbRadius * 2f;
            float t = Maximum > Minimum ? (float)(Value - Minimum) / (Maximum - Minimum) : 0f;
            float thumbX = ThumbRadius + t * usable;
            float midY = Height / 2f;

            using (var trackPen = new Pen(TrackColor, 4f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawLine(trackPen, ThumbRadius, midY, Width - ThumbRadius, midY);
            using (var borderPen = new Pen(TrackBorder, 1f))
                g.DrawLine(borderPen, ThumbRadius, midY - 2, Width - ThumbRadius, midY - 2);

            using (var fillPen = new Pen(FillColor, 4f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawLine(fillPen, ThumbRadius, midY, thumbX, midY);

            if (Focused)
            {
                using var glow = new SolidBrush(Color.FromArgb(55, FillColor));
                g.FillEllipse(glow, thumbX - ThumbRadius - 3, midY - ThumbRadius - 3, (ThumbRadius + 3) * 2, (ThumbRadius + 3) * 2);
            }

            using (var thumbBrush = new SolidBrush(FillColor))
                g.FillEllipse(thumbBrush, thumbX - ThumbRadius, midY - ThumbRadius, ThumbRadius * 2, ThumbRadius * 2);
            using (var thumbPen = new Pen(ThumbOutline, 2f))
                g.DrawEllipse(thumbPen, thumbX - ThumbRadius + 1, midY - ThumbRadius + 1, ThumbRadius * 2 - 2, ThumbRadius * 2 - 2);
        }
    }
}
