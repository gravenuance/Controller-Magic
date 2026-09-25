namespace ControllerMagic
{
    // Comma-separated text was hard to scan and fiddly to edit; this shows each entry as a
    // removable chip with a trailing text box to add new ones (Enter commits). Adding or removing
    // touches only that one chip, so the add box keeps focus between entries.
    internal sealed class ChipList : FlowLayoutPanel
    {
        private readonly List<string> _items = new();
        private readonly TextBox _addBox;

        public event Action<List<string>>? ItemsChanged;

        public Color ChipBg { get; set; } = Color.FromArgb(0x14, 0x16, 0x1A);
        public Color ChipBorder { get; set; } = Color.FromArgb(0x2C, 0x30, 0x38);
        public Color ChipText { get; set; } = Color.FromArgb(0xE8, 0xEA, 0xED);
        public Color MutedText { get; set; } = Color.FromArgb(0x86, 0x8F, 0xA0);
        public Color FocusColor { get; set; } = Color.FromArgb(0xE8, 0xA3, 0x3D);

        public List<string> Items => new(_items);

        public ChipList()
        {
            FlowDirection = FlowDirection.LeftToRight;
            WrapContents = true;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(7);
            AccessibleRole = AccessibleRole.Grouping;

            _addBox = new TextBox
            {
                BorderStyle = BorderStyle.None,
                Width = 110,
                Margin = new Padding(4, 6, 3, 3),
                PlaceholderText = "+ add…",
                AccessibleName = "Add entry",
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
            Controls.Add(_addBox);
        }

        public void ApplyPalette(Color chipBg, Color chipBorder, Color chipText, Color muted, Color fieldBg, Color focus)
        {
            ChipBg = chipBg;
            ChipBorder = chipBorder;
            ChipText = chipText;
            MutedText = muted;
            FocusColor = focus;
            BackColor = fieldBg;
            _addBox.BackColor = fieldBg;
            _addBox.ForeColor = chipText;
        }

        public void SetItems(IEnumerable<string> items)
        {
            ArgumentNullException.ThrowIfNull(items);

            SuspendLayout();
            foreach (var chip in Controls.OfType<Chip>().ToList())
            {
                Controls.Remove(chip);
                chip.Dispose();
            }

            _items.Clear();
            foreach (var item in items)
            {
                _items.Add(item);
                InsertChip(item);
            }
            ResumeLayout();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using var pen = new Pen(ChipBorder);
            e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }

        internal void AddItem(string item)
        {
            if (_items.Any(i => string.Equals(i, item, StringComparison.OrdinalIgnoreCase)))
                return;

            _items.Add(item);
            InsertChip(item);
            ItemsChanged?.Invoke(Items);
        }

        // Chips sit before the add box, in item order.
        private void InsertChip(string item)
        {
            var chip = new Chip(item)
            {
                ChipBg = ChipBg,
                ChipBorder = ChipBorder,
                ChipText = ChipText,
                MutedText = MutedText,
                FocusColor = FocusColor,
            };
            chip.RemoveRequested += RemoveChip;
            Controls.Add(chip);
            Controls.SetChildIndex(chip, Controls.GetChildIndex(_addBox));
        }

        private void RemoveChip(Chip chip)
        {
            int index = Controls.GetChildIndex(chip, throwException: false);
            if (index < 0)
                return;

            bool hadFocus = chip.ContainsFocus;
            _items.RemoveAt(index);
            Controls.Remove(chip);

            // Keyboard removal hands focus to the next chip, or the add box after the last one.
            if (hadFocus)
                Controls[Math.Min(index, Controls.Count - 1)].Focus();

            // The chip is still inside its own click or key handler; dispose once that unwinds.
            if (chip.IsHandleCreated)
                chip.BeginInvoke(chip.Dispose);
            else
                chip.Dispose();

            ItemsChanged?.Invoke(Items);
        }
    }
}
