using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ControllerMagic
{
    internal sealed partial class KeyboardOverlayForm : Form
    {

        private readonly ControllerPoller _poller;
        private readonly System.Windows.Forms.Timer _timer;
        private bool _wasKeyboardMode;

        // BackColor/TransparencyKey below must stay pure black - that exact color is the chroma
        // key that makes the rest of the window invisible, so only these drawn tiles show up
        // floating over the desktop. Everything drawn onto it follows the amber instrument-panel
        // palette used everywhere else now, in place of the old neon lime.
        private readonly SolidBrush _textBrush = new(Theme.Ink);
        private readonly SolidBrush _hotBrush = new(Color.FromArgb(220, Theme.Accent));
        private readonly SolidBrush _normalBrush = new(Color.FromArgb(190, Theme.Surface));
        private readonly Pen _pen = new(Theme.Accent, 1.5f);
        private readonly Font _tileFont = new("Segoe UI", 16f, FontStyle.Bold);
        private readonly Font _legendFont = new("Segoe UI", 12f, FontStyle.Regular);
        private readonly StringFormat _centerFormat = new()
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };

        public KeyboardOverlayForm(ControllerPoller poller)
        {
            InitializeComponent();

            _poller = poller;

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;

            BackColor = Color.Black;
            TransparencyKey = Color.Black;

            StartPosition = FormStartPosition.Manual;
            Size = new Size(500, 500);

            DoubleBuffered = true;

            Load += (_, __) => MakeClickThrough();

            // Only repaint while keyboard mode is active (for the live sector highlight),
            // plus one final tick on the transition out to clear the last frame.
            _timer = new System.Windows.Forms.Timer { Interval = 16 }; // ~60 FPS
            _timer.Tick += (_, __) =>
            {
                bool isKeyboardMode = _poller.KeyboardMode;
                if (isKeyboardMode || _wasKeyboardMode)
                    Invalidate();
                _wasKeyboardMode = isKeyboardMode;
            };
            _timer.Start();
        }

        // Make the form click-through so it does not steal mouse input
        private void MakeClickThrough()
        {
            int exStyle = (int)GetWindowLong(Handle, GWL_EXSTYLE);
            exStyle |= WS_EX_TRANSPARENT | WS_EX_LAYERED;
            SetWindowLong(Handle, GWL_EXSTYLE, (IntPtr)exStyle);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            if (!_poller.KeyboardMode)
                return;

            var layout = ControllerPoller.KeyboardLayout;
            int layer = _poller.KeyboardLayer;
            int hot = _poller.CurrentSector;
            int slot = _poller.SlotIndex;

            float cx = ClientSize.Width / 2f;
            float cy = ClientSize.Height / 2f;
            float baseRadius = 120f; // a bit farther out

            float tileSize = 40f;

            for (int sector = 0; sector < 8; sector++)
            {
                bool isHot = (sector == hot);

                double angleDeg = 90.0 + sector * 45.0;

                double angleRad = angleDeg * Math.PI / 180.0;

                for (int index = 0; index < 4; index++)
                {

                    var entry = layout[layer, sector, index];
                    if (entry.Vk == 0)
                        continue;
                    bool isSelectedSlot = (index == slot);
                    float inner = baseRadius + index * (tileSize + 4f);
                    float outer = inner + tileSize;
                    float midR = (inner + outer) / 2f;

                    float x = cx + (float)(midR * Math.Cos(angleRad));
                    float y = cy - (float)(midR * Math.Sin(angleRad));

                    var rect = new RectangleF(
                        x - tileSize / 2f,
                        y - tileSize / 2f,
                        tileSize,
                        tileSize
                    );

                    g.FillEllipse(isHot && isSelectedSlot ? _hotBrush : _normalBrush, rect);
                    g.DrawEllipse(_pen, rect);

                    g.DrawString(entry.Display.ToString(), _tileFont, _textBrush, rect, _centerFormat);
                }
            }

            DrawLegend(g);
        }

        private void DrawLegend(Graphics g)
        {
            const string legend = "X = ⌫   Y = ␣   B = .";

            var size = g.MeasureString(legend, _legendFont);
            float x = ClientSize.Width - size.Width - 20;
            float y = ClientSize.Height - size.Height - 20;

            g.DrawString(legend, _legendFont, _textBrush, x, y);
        }

        // Win32 interop for click-through
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_LAYERED = 0x00080000;

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern IntPtr SetWindowLong(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
    }
}
