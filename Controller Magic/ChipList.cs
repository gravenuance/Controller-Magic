namespace ControllerMagic
{
    // Comma-separated text was hard to scan and fiddly to edit; this shows each entry as a
    // removable chip with a trailing text box to add new ones (Enter commits).
    internal sealed class ChipList : FlowLayoutPanel
    {
        private readonly List<string> _items = new();
        private readonly TextBox _addBox;

        public event Action<List<string>>? ItemsChanged;

        public Color ChipBg { get; set; } = Color.FromArgb(0x14, 0x16, 0x1A);
        public Color ChipBorder { get; set; } = Color.FromArgb(0x2C, 0x30, 0x38);
        public Color ChipText { get; set; } = Color.FromArgb(0xE8, 0xEA, 0xED);
        public Color MutedText { get; set; } = Color.FromArgb(0x86, 0x8F, 0xA0);

        public List<string> Items => new(_items);

        public ChipList()
        {
            FlowDirection = FlowDirection.LeftToRight;
            WrapContents = true;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(7);

            _addBox = new TextBox
            {
                BorderStyle = BorderStyle.None,
                Width = 110,
                Margin = new Padding(4, 6, 3, 3),
                PlaceholderText = "+ add…"
            };
            _addBox.KeyDown += (_, e) =>
            {
                if (e.KeyCode != Keys.Enter) return;
                e.SuppressKeyPress = true;
                string text = _addBox.Text.Trim();
                if (text.Length > 0)
                {
                    AddItem(text);
                    _addBox.Clear();
                }
            };
        }

        public void ApplyPalette(Color chipBg, Color chipBorder, Color chipText, Color muted, Color fieldBg)
        {
            ChipBg = chipBg;
            ChipBorder = chipBorder;
            ChipText = chipText;
            MutedText = muted;
            BackColor = fieldBg;
            _addBox.BackColor = fieldBg;
            _addBox.ForeColor = chipText;
        }

        public void SetItems(IEnumerable<string> items)
        {
            _items.Clear();
            _items.AddRange(items);
            Rebuild();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using var pen = new Pen(ChipBorder);
            e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }

        private void AddItem(string item)
        {
            if (_items.Any(i => string.Equals(i, item, StringComparison.OrdinalIgnoreCase)))
                return;

            _items.Add(item);
            Rebuild();
            ItemsChanged?.Invoke(Items);
        }

        private void RemoveItem(string item)
        {
            _items.Remove(item);
            Rebuild();
            ItemsChanged?.Invoke(Items);
        }

        private void Rebuild()
        {
            SuspendLayout();
            Controls.Clear();
            foreach (var item in _items)
            {
                var chip = new Chip(item)
                {
                    ChipBg = ChipBg,
                    ChipBorder = ChipBorder,
                    ChipText = ChipText,
                    MutedText = MutedText
                };
                chip.RemoveRequested += () => RemoveItem(item);
                Controls.Add(chip);
            }
            Controls.Add(_addBox);
            ResumeLayout();
            Invalidate();
        }
    }
}
