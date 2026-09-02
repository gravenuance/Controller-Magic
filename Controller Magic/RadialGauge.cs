using System.Drawing.Drawing2D;

namespace ControllerMagic
{
    // Deadzone is literally a radius on the stick's 2D plane, so a ring diagram reads faster than
    // a bare number - the filled circle is the dead center, the outer ring is full stick throw.
    internal sealed class RadialGauge : Control
    {
        public double Fraction
        {
            get;
            set { field = Math.Clamp(value, 0.0, 1.0); Invalidate(); }
        }

        public Color RingColor { get; set; } = Color.FromArgb(46, 255, 255, 255);
        public Color FillColor { get; set; } = Color.FromArgb(0xE8, 0xA3, 0x3D);
        public Color PanelColor { get; set; } = Color.FromArgb(0x22, 0x26, 0x2D);
        public Color BorderColor { get; set; } = Color.FromArgb(0x2C, 0x30, 0x38);

        public RadialGauge()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            Size = new Size(64, 34);

            // Purely decorative - it mirrors the paired Slider's own value, which a screen reader
            // already reports via that control's AccessibleObject. Hidden rather than announced as
            // an unlabeled, redundant "graphic".
            AccessibleRole = AccessibleRole.None;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            using (var bg = new SolidBrush(PanelColor))
                g.FillRectangle(bg, ClientRectangle);
            using (var borderPen = new Pen(BorderColor))
                g.DrawRectangle(borderPen, 0, 0, Width - 1, Height - 1);

            float cx = Width / 2f, cy = Height / 2f;
            float rOuter = Height / 2f - 3f;

            using (var ringPen = new Pen(RingColor, 1f))
                g.DrawEllipse(ringPen, cx - rOuter, cy - rOuter, rOuter * 2, rOuter * 2);

            float rInner = Math.Max((float)(rOuter * Fraction), 1.4f);
            using var fillBrush = new SolidBrush(Color.FromArgb(217, FillColor));
            g.FillEllipse(fillBrush, cx - rInner, cy - rInner, rInner * 2, rInner * 2);
        }
    }
}
