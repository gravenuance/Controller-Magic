using System.Drawing.Drawing2D;

namespace ControllerMagic
{
    // A single text glyph that works as a push button, e.g. the Settings close "✕": flat like a
    // label, brightened on hover or focus, a tab stop, and an IButtonControl so a form can make it
    // its CancelButton for Esc.
    internal sealed class GlyphButton : Control, IButtonControl
    {
        private bool _hovered;

        public Color HoverColor { get; set; } = Theme.Ink;
        public Color FocusColor { get; set; } = Theme.Accent;

        public GlyphButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.Selectable | ControlStyles.StandardClick, true);
            TabStop = true;
            Cursor = Cursors.Hand;
            AccessibleRole = AccessibleRole.PushButton;
        }

        public DialogResult DialogResult { get; set; }

        internal static bool IsActivationKey(Keys key) => key is Keys.Enter or Keys.Space;

        public void NotifyDefault(bool value)
        {
        }

        public void PerformClick()
        {
            if (Enabled)
                OnClick(EventArgs.Empty);
        }

        // Handled here rather than left to the form, so Enter can't also reach a default button.
        protected override bool IsInputKey(Keys keyData) => IsActivationKey(keyData) || base.IsInputKey(keyData);

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Handled || !IsActivationKey(e.KeyData))
                return;

            e.Handled = true;
            e.SuppressKeyPress = true;
            PerformClick();
        }

        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hovered = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hovered = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override AccessibleObject CreateAccessibilityInstance() => new GlyphButtonAccessibleObject(this);

        // Lets a screen reader's "activate" press the button.
        private sealed class GlyphButtonAccessibleObject(GlyphButton owner) : ControlAccessibleObject(owner)
        {
            public override void DoDefaultAction() => owner.PerformClick();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Color glyphColor = _hovered || Focused ? HoverColor : ForeColor;
            TextRenderer.DrawText(g, Text, Font, ClientRectangle, glyphColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);

            if (!Focused)
                return;

            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = ToggleSwitch.RoundedRect(new Rectangle(1, 1, Width - 3, Height - 3), 6);
            using var focusPen = new Pen(Color.FromArgb(160, FocusColor), 1.5f) { DashStyle = DashStyle.Dot };
            g.DrawPath(focusPen, path);
        }
    }
}
