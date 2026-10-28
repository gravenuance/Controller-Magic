using System.Drawing.Drawing2D;

namespace ControllerMagic;

// A vertical list of icon-and-name items with one selected, e.g. the Settings page navigation. One
// tab stop; Up/Down/Home/End move the selection, which follows focus like a native list.
internal sealed class NavList : Control
{
    internal readonly record struct Item(string Name, string Glyph);

    private const int IconWidth = 28;
    private const int ItemInset = 8;
    private const float ItemRadius = 6f;

    private Item[] _items = [];
    private AccessibleObject?[] _itemObjects = [];
    private int _hot = -1;

    // Focus ring only after a keyboard action, so a click doesn't leave one behind.
    private bool _showFocus = true;

    public NavList()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.Selectable | ControlStyles.ResizeRedraw, true);
        TabStop = true;
        BackColor = Theme.Surface;
        Font = Theme.UiFont;
        AccessibleRole = AccessibleRole.List;
    }

    // Raised whenever the selection changes, by the user or in code.
    public event EventHandler? SelectedIndexChanged;

    public int SelectedIndex
    {
        get;
        set
        {
            if (_items.Length == 0)
                return;
            int clamped = Math.Clamp(value, 0, _items.Length - 1);
            if (field == clamped)
                return;
            field = clamped;
            Invalidate();
            AccessibilityNotifyClients(AccessibleEvents.Selection, clamped);
            AccessibilityNotifyClients(AccessibleEvents.Focus, clamped);
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    // Height that shows every item, for a parent that sizes itself to its content.
    public static int ContentHeight(int itemCount) => itemCount * Theme.NavItemHeight + Theme.NavPadding * 2;

    public void SetItems(IReadOnlyList<Item> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        _items = [.. items];
        _itemObjects = new AccessibleObject[_items.Length];
        _hot = -1;
        SelectedIndex = Math.Min(SelectedIndex, Math.Max(0, _items.Length - 1));
        Invalidate();
    }

    // The index a navigation key moves to, never leaving 0..count-1.
    internal static int Navigate(int current, Keys key, int count)
    {
        if (count <= 0)
            return -1;
        return key switch
        {
            Keys.Up => Math.Max(0, current - 1),
            Keys.Down => Math.Min(count - 1, current + 1),
            Keys.Home => 0,
            Keys.End => count - 1,
            _ => current,
        };
    }

    internal static bool IsNavigationKey(Keys key) => key is Keys.Up or Keys.Down or Keys.Home or Keys.End;

    // Layout constants are unscaled pixels; the control's own DPI scales them, since the painting is custom.
    private int Scaled(int pixels) => (int)Math.Round(pixels * DeviceDpi / 96.0);

    private Rectangle ItemBounds(int index) =>
        new(Scaled(ItemInset), Scaled(Theme.NavPadding) + index * Scaled(Theme.NavItemHeight),
            Width - Scaled(ItemInset) * 2 - 1, Scaled(Theme.NavItemHeight));

    private int IndexAt(Point point)
    {
        int top = Scaled(Theme.NavPadding);
        int index = (point.Y - top) / Scaled(Theme.NavItemHeight);
        return point.Y >= top && index < _items.Length && ItemBounds(index).Contains(point) ? index : -1;
    }

    // Handled here so the form's own dialog navigation doesn't take the arrow keys for focus movement.
    protected override bool IsInputKey(Keys keyData) => IsNavigationKey(keyData) || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || !IsNavigationKey(e.KeyData))
            return;

        e.Handled = true;
        e.SuppressKeyPress = true;
        _showFocus = true;
        int target = Navigate(SelectedIndex, e.KeyData, _items.Length);
        if (target >= 0 && target != SelectedIndex)
            SelectedIndex = target;
        else
            Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        _showFocus = false;
        Focus();
        int index = IndexAt(e.Location);
        if (e.Button == MouseButtons.Left && index >= 0)
            SelectedIndex = index;
        Invalidate();
        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int index = IndexAt(e.Location);
        if (index != _hot)
        {
            _hot = index;
            Cursor = index >= 0 ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        if (_hot >= 0)
        {
            _hot = -1;
            Invalidate();
        }
        base.OnMouseLeave(e);
    }

    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        using (var edge = new Pen(Theme.Line))
            g.DrawLine(edge, Width - 1, 0, Width - 1, Height);

        for (int i = 0; i < _items.Length; i++)
            PaintItem(g, i);
    }

    private void PaintItem(Graphics g, int index)
    {
        var bounds = ItemBounds(index);
        bool selected = index == SelectedIndex;
        bool hot = index == _hot;

        if (selected || hot)
        {
            using var path = ToggleSwitch.RoundedRect(bounds, ItemRadius);
            using var fill = new SolidBrush(selected ? Theme.NavSelected : Theme.Surface2);
            g.FillPath(fill, path);
        }

        if (selected)
        {
            using var bar = new SolidBrush(Theme.Accent);
            var barRect = new Rectangle(bounds.Left + Scaled(3), bounds.Top + Scaled(10), Scaled(3), bounds.Height - Scaled(20));
            using var barPath = ToggleSwitch.RoundedRect(barRect, Scaled(3) / 2f);
            g.FillPath(bar, barPath);
        }

        Color textColor = selected || hot ? Theme.Ink : Theme.Muted;
        Color iconColor = selected ? Theme.Accent : textColor;
        var iconRect = new Rectangle(bounds.Left + Scaled(14), bounds.Top, Scaled(IconWidth), bounds.Height);
        var textRect = new Rectangle(iconRect.Right + Scaled(6), bounds.Top, bounds.Right - iconRect.Right - Scaled(10), bounds.Height);
        const TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
        TextRenderer.DrawText(g, _items[index].Glyph, Theme.NavIconFont, iconRect, iconColor, flags | TextFormatFlags.HorizontalCenter);
        TextRenderer.DrawText(g, _items[index].Name, selected ? Theme.UiFontBold : Theme.UiFont, textRect, textColor,
            flags | TextFormatFlags.EndEllipsis);

        if (selected && Focused && _showFocus)
        {
            var ring = Rectangle.Inflate(bounds, -1, -1);
            using var path = ToggleSwitch.RoundedRect(ring, ItemRadius);
            using var pen = new Pen(Theme.Accent, 1.5f);
            g.DrawPath(pen, path);
        }
    }

    protected override AccessibleObject CreateAccessibilityInstance() => new NavListAccessibleObject(this);

    private AccessibleObject ItemObject(int index) => _itemObjects[index] ??= new NavItemAccessibleObject(this, index);

    private sealed class NavListAccessibleObject(NavList owner) : ControlAccessibleObject(owner)
    {
        public override AccessibleRole Role => AccessibleRole.List;

        public override int GetChildCount() => owner._items.Length;

        public override AccessibleObject? GetChild(int index) =>
            index >= 0 && index < owner._items.Length ? owner.ItemObject(index) : null;

        public override AccessibleObject? GetSelected() =>
            owner._items.Length == 0 ? null : owner.ItemObject(owner.SelectedIndex);

        public override AccessibleObject? GetFocused() => owner.Focused ? GetSelected() : null;

        public override AccessibleObject? HitTest(int x, int y)
        {
            int index = owner.IndexAt(owner.PointToClient(new Point(x, y)));
            return index >= 0 ? owner.ItemObject(index) : base.HitTest(x, y);
        }
    }

    private sealed class NavItemAccessibleObject(NavList owner, int index) : AccessibleObject
    {
        public override string Name => owner._items[index].Name;

        public override AccessibleRole Role => AccessibleRole.ListItem;

        public override AccessibleObject Parent => owner.AccessibilityObject;

        public override Rectangle Bounds => owner.RectangleToScreen(owner.ItemBounds(index));

        public override AccessibleStates State
        {
            get
            {
                var state = AccessibleStates.Selectable | AccessibleStates.Focusable;
                if (index == owner.SelectedIndex)
                    state |= AccessibleStates.Selected | (owner.Focused ? AccessibleStates.Focused : AccessibleStates.None);
                return state;
            }
        }

        public override string DefaultAction => "Select";

        public override void DoDefaultAction() => owner.SelectedIndex = index;

        public override void Select(AccessibleSelection flags)
        {
            if ((flags & (AccessibleSelection.TakeSelection | AccessibleSelection.AddSelection)) != 0)
                owner.SelectedIndex = index;
            if ((flags & AccessibleSelection.TakeFocus) != 0)
                owner.Focus();
        }

        public override AccessibleObject? Navigate(AccessibleNavigation navdir) => navdir switch
        {
            AccessibleNavigation.Next or AccessibleNavigation.Down => index + 1 < owner._items.Length ? owner.ItemObject(index + 1) : null,
            AccessibleNavigation.Previous or AccessibleNavigation.Up => index > 0 ? owner.ItemObject(index - 1) : null,
            _ => null,
        };
    }
}
