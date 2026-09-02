using System.Drawing.Drawing2D;

namespace ControllerMagic
{
    // Live sparkline for the acceleration/ramp-up curves - a bare exponent or duration is
    // meaningless on its own, but the actual response shape is immediately readable.
    internal sealed class CurvePreview : Control
    {
        public Func<double, double>? CurveFn { get; set; }
        public Color LineColor { get; set; } = Color.FromArgb(0xE8, 0xA3, 0x3D);
        public Color GridColor { get; set; } = Color.FromArgb(28, 255, 255, 255);
        public Color PanelColor { get; set; } = Color.FromArgb(0x22, 0x26, 0x2D);
        public Color BorderColor { get; set; } = Color.FromArgb(0x2C, 0x30, 0x38);

        public CurvePreview()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            Size = new Size(64, 34);

            // Purely decorative - it mirrors the paired Slider's own value, which a screen reader
            // already reports via that control's AccessibleObject. Hidden rather than announced as
            // an unlabeled, redundant "graphic".
            AccessibleRole = AccessibleRole.None;
        }

        public void Redraw() => Invalidate();

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            using (var bg = new SolidBrush(PanelColor))
                g.FillRectangle(bg, ClientRectangle);
            using (var borderPen = new Pen(BorderColor))
                g.DrawRectangle(borderPen, 0, 0, Width - 1, Height - 1);

            using (var gridPen = new Pen(GridColor))
            {
                for (int i = 1; i < 3; i++)
                {
                    float y = Height / 3f * i;
                    g.DrawLine(gridPen, 4, y, Width - 4, y);
                }
            }

            if (CurveFn == null) return;

            const int steps = 40;
            const float pad = 4f;
            var pts = new PointF[steps + 1];
            for (int i = 0; i <= steps; i++)
            {
                double t = i / (double)steps;
                double y = Math.Clamp(CurveFn(t), 0.0, 1.0);
                float px = pad + (float)t * (Width - pad * 2);
                float py = Height - pad - (float)y * (Height - pad * 2);
                pts[i] = new PointF(px, py);
            }

            using (var linePen = new Pen(LineColor, 1.6f) { LineJoin = LineJoin.Round })
                g.DrawLines(linePen, pts);

            using var dotBrush = new SolidBrush(LineColor);
            var end = pts[^1];
            g.FillEllipse(dotBrush, end.X - 2f, end.Y - 2f, 4f, 4f);
        }
    }
}
