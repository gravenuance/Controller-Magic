using System.Drawing.Drawing2D;

namespace ControllerMagic
{
    // A rounded, bordered, filled group panel - replacing the old flat divider-line sections. A
    // bordered surface reads as one unit before a single label inside it is even read.
    internal sealed class CardPanel : Panel
    {
        public Color BorderColor { get; set; } = Theme.Line;
        public float CornerRadius { get; set; } = 8f;

        public CardPanel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Surface;
            AccessibleRole = AccessibleRole.Grouping;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (Width > 0 && Height > 0)
            {
                using var path = ToggleSwitch.RoundedRect(new Rectangle(0, 0, Width, Height), CornerRadius);
                var old = Region;
                Region = new Region(path);
                old?.Dispose();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using var path = ToggleSwitch.RoundedRect(rect, CornerRadius);
            using var pen = new Pen(BorderColor);
            g.DrawPath(pen, path);
        }
    }
}
