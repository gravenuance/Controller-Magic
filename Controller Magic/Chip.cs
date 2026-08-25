using System.Drawing.Drawing2D;

namespace ControllerMagic
{
    // One removable pill in a ChipList. Self-draws its own "x" glyph rather than composing a
    // separate button, so remove-hit-testing is just "did the click land in the glyph rectangle."
    internal sealed class Chip : Control
    {
        public event Action? RemoveRequested;

        private Rectangle _closeHit;
        private static readonly Font MonoFont = new("Consolas", 8.25f);

        public Color ChipBg { get; set; } = Color.FromArgb(0x14, 0x16, 0x1A);
        public Color ChipBorder { get; set; } = Color.FromArgb(0x2C, 0x30, 0x38);
        public Color ChipText { get; set; } = Color.FromArgb(0xE8, 0xEA, 0xED);
        public Color MutedText { get; set; } = Color.FromArgb(0x86, 0x8F, 0xA0);

        public Chip(string text)
        {
            Text = text;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            Margin = new Padding(3);
            Height = 22;

            // CreateGraphics() would measure against the same real-screen DPI, but as a side
            // effect it also forces this control's native window handle into existence right here
            // in the constructor, before it's ever parented or shown - wasteful given ChipList
            // rebuilds its whole Chip collection on every add/remove. The desktop DC measures
            // identically without touching this control's handle at all.
            using var g = Graphics.FromHwnd(IntPtr.Zero);
            var textSize = g.MeasureString(Text, MonoFont);
            Width = (int)textSize.Width + 9 + 22;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = ToggleSwitch.RoundedRect(rect, Height / 2f))
            {
                using var bgBrush = new SolidBrush(ChipBg);
                g.FillPath(bgBrush, path);
                using var borderPen = new Pen(ChipBorder);
                g.DrawPath(borderPen, path);
            }

            using (var textBrush = new SolidBrush(ChipText))
                g.DrawString(Text, MonoFont, textBrush, 9, (Height - MonoFont.Height) / 2f - 1);

            _closeHit = new Rectangle(Width - 20, 3, 16, 16);
            bool hover = _closeHit.Contains(PointToClient(MousePosition));
            if (hover)
            {
                using var hoverBrush = new SolidBrush(ChipBorder);
                g.FillEllipse(hoverBrush, _closeHit);
            }

            using var xPen = new Pen(hover ? ChipText : MutedText, 1.3f);
            int cx = _closeHit.X + _closeHit.Width / 2, cy = _closeHit.Y + _closeHit.Height / 2;
            g.DrawLine(xPen, cx - 3, cy - 3, cx + 3, cy + 3);
            g.DrawLine(xPen, cx - 3, cy + 3, cx + 3, cy - 3);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            Invalidate();
            Cursor = _closeHit.Contains(e.Location) ? Cursors.Hand : Cursors.Default;
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnClick(EventArgs e)
        {
            if (_closeHit.Contains(PointToClient(MousePosition)))
                RemoveRequested?.Invoke();
            base.OnClick(e);
        }
    }
}
