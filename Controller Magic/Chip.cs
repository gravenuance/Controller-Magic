using System.Drawing.Drawing2D;

namespace ControllerMagic
{
    // One removable pill in a ChipList. Self-draws its own "x" glyph rather than composing a
    // separate button, so remove-hit-testing is just "did the click land in the glyph rectangle."
    // Also a tab stop: Delete, Backspace, Enter or Space removes it from the keyboard.
    internal sealed class Chip : Control
    {
        public event Action<Chip>? RemoveRequested;

        // Follows the chip's current size, which DPI changes and autoscaling can alter after construction.
        private Rectangle CloseHit => new(Width - 20, 3, 16, 16);

        public Color ChipBg { get; set; } = Color.FromArgb(0x14, 0x16, 0x1A);
        public Color ChipBorder { get; set; } = Color.FromArgb(0x2C, 0x30, 0x38);
        public Color ChipText { get; set; } = Color.FromArgb(0xE8, 0xEA, 0xED);
        public Color MutedText { get; set; } = Color.FromArgb(0x86, 0x8F, 0xA0);
        public Color FocusColor { get; set; } = Color.FromArgb(0xE8, 0xA3, 0x3D);

        public Chip(string text)
        {
            Text = text;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.Selectable, true);
            TabStop = true;
            Margin = new Padding(3);
            Height = 22;
            AccessibleRole = AccessibleRole.PushButton;
            AccessibleName = $"Remove {text}";
            AccessibleDefaultActionDescription = "Remove";

            // The desktop DC measures the same as CreateGraphics() without forcing this control's
            // window handle into existence before it's even parented.
            using var g = Graphics.FromHwnd(IntPtr.Zero);
            var textSize = g.MeasureString(Text, Theme.ChipFont);
            Width = (int)textSize.Width + 9 + 22;
        }

        internal static bool IsRemoveKey(Keys key) => key is Keys.Delete or Keys.Back or Keys.Enter or Keys.Space;

        internal void RequestRemove() => RemoveRequested?.Invoke(this);

        protected override bool IsInputKey(Keys keyData) => IsRemoveKey(keyData) || base.IsInputKey(keyData);

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Handled || !IsRemoveKey(e.KeyData))
                return;

            e.Handled = true;
            e.SuppressKeyPress = true;
            RequestRemove();
        }

        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override AccessibleObject CreateAccessibilityInstance() => new ChipAccessibleObject(this);

        // Lets a screen reader's "activate" remove the chip, matching its "Remove <name>" label.
        private sealed class ChipAccessibleObject(Chip owner) : ControlAccessibleObject(owner)
        {
            public override void DoDefaultAction() => owner.RequestRemove();
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
                using var borderPen = new Pen(Focused ? FocusColor : ChipBorder, Focused ? 1.5f : 1f);
                g.DrawPath(borderPen, path);
            }

            using (var textBrush = new SolidBrush(ChipText))
                g.DrawString(Text, Theme.ChipFont, textBrush, 9, (Height - Theme.ChipFont.Height) / 2f - 1);

            bool highlight = Focused || CloseHit.Contains(PointToClient(MousePosition));
            if (highlight)
            {
                using var hoverBrush = new SolidBrush(ChipBorder);
                g.FillEllipse(hoverBrush, CloseHit);
            }

            using var xPen = new Pen(highlight ? ChipText : MutedText, 1.3f);
            int cx = CloseHit.X + CloseHit.Width / 2, cy = CloseHit.Y + CloseHit.Height / 2;
            g.DrawLine(xPen, cx - 3, cy - 3, cx + 3, cy + 3);
            g.DrawLine(xPen, cx - 3, cy + 3, cx + 3, cy - 3);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            Invalidate();
            Cursor = CloseHit.Contains(e.Location) ? Cursors.Hand : Cursors.Default;
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button == MouseButtons.Left && CloseHit.Contains(e.Location))
                RequestRemove();
        }
    }
}
