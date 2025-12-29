using System.Runtime.InteropServices;

namespace ControllerMagic
{
    internal partial class KeyboardOverlayForm : Form
    {

        private readonly ControllerPoller _poller;
        private readonly System.Windows.Forms.Timer _timer;

        public KeyboardOverlayForm(ControllerPoller poller)
        {
            InitializeComponent();

            _poller = poller;

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;

            BackColor = Color.Black;
            TransparencyKey = Color.Black;

            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(500, 500);

            DoubleBuffered = true;

            Load += (_, __) => MakeClickThrough();

            _timer = new System.Windows.Forms.Timer { Interval = 16 }; // ~60 FPS
            _timer.Tick += (_, __) => Invalidate();
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

            float cx = ClientSize.Width / 2f;
            float cy = ClientSize.Height / 2f;
            float baseRadius = 120f; // a bit farther out

            using var textBrush = new SolidBrush(Color.Lime);                       // letters
            using var hotBrush = new SolidBrush(Color.FromArgb(200, 0, 255, 0));
            using var normalBrush = new SolidBrush(Color.FromArgb(178, 10, 10, 10));
            using var pen = new Pen(Color.Lime, 1.5f);                        // outline
            using var font = new Font("Segoe UI", 16f, FontStyle.Bold);        // slightly larger text

            float tileSize = 40f;

            for (int sector = 0; sector < 8; sector++)
            {
                bool isHot = (sector == hot);
                int slot = _poller.SlotIndex;

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

                    g.FillEllipse(isHot && isSelectedSlot ? hotBrush : normalBrush, rect);
                    g.DrawEllipse(pen, rect);

                    var sf = new StringFormat
                    {
                        Alignment = StringAlignment.Center,
                        LineAlignment = StringAlignment.Center
                    };

                    g.DrawString(entry.Display.ToString(), font, textBrush, rect, sf);
                }
            }

            DrawLegend(g);
        }

        private void DrawLegend(Graphics g)
        {
            const string legend = "X = ⌫   Y = ␣   B = .";
            using var brush = new SolidBrush(Color.Lime);
            using var font = new Font("Segoe UI", 12f, FontStyle.Regular);

            var size = g.MeasureString(legend, font);
            float x = ClientSize.Width - size.Width - 20;
            float y = ClientSize.Height - size.Height - 20;

            g.DrawString(legend, font, brush, x, y);
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
