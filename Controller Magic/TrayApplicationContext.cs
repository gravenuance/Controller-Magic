using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace ControllerMagic
{
    internal sealed class TrayApplicationContext : ApplicationContext
    {
        // Segoe MDL2 Assets glyphs - gear, refresh arrows, plain X - rendered to small bitmaps
        // for the space to the left of each context-menu item. ToolStripItem doesn't dispose its
        // own Image, so these are tracked and disposed alongside the tray icon below.
        private const char GearGlyph = '';
        private const char RefreshGlyph = '';
        private const char CancelGlyph = '';

        private readonly NotifyIcon _trayIcon;
        private readonly ControllerPoller _controllerPoller;
        private readonly KeyboardOverlayForm _overlay;
        private readonly Bitmap _settingsIcon;
        private readonly Bitmap _restartIcon;
        private readonly Bitmap _exitIcon;

        public TrayApplicationContext()
        {
            StartupHelper.EnsureMigrated();

            _trayIcon = new NotifyIcon
            {
                Icon = Theme.AppIcon,
                Text = "Controller Magic",
                Visible = true
            };

            _settingsIcon = CreateMenuIcon(GearGlyph);
            _restartIcon = CreateMenuIcon(RefreshGlyph);
            _exitIcon = CreateMenuIcon(CancelGlyph);

            var menu = new ContextMenuStrip();

            var settingsItem = new ToolStripMenuItem("Settings...", _settingsIcon, OnSettingsClick);
            var restartItem = new ToolStripMenuItem("Restart", _restartIcon, OnRestartClick);
            var exitItem = new ToolStripMenuItem("Exit", _exitIcon, OnExitClick);

            menu.Items.Add(settingsItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(restartItem);
            menu.Items.Add(exitItem);

            _trayIcon.ContextMenuStrip = menu;
            _trayIcon.DoubleClick += OnSettingsClick;

            EnsureStartupConfigured();

            _controllerPoller = new ControllerPoller();
            _controllerPoller.KeyboardModeChanged += OnKeyboardModeChanged;
            _controllerPoller.Start();

            _overlay = new KeyboardOverlayForm(_controllerPoller);
            _overlay.Show();
        }

        // Runs once, ever, the first time the app starts: if startup has never been configured,
        // default it to on - no prompt. StartupHelper.SetEnabled already handles the unelevated
        // task attempt, the UAC-elevated retry if that's denied, and the Run-key fallback if
        // elevation is declined, so this can still surface a UAC prompt on locked-down machines;
        // it just isn't an app-level dialog asking permission first.
        private static void EnsureStartupConfigured()
        {
            if (AppSettings.Instance.HasInitializedStartup)
                return;

            AppSettings.Instance.HasInitializedStartup = true;
            StartupHelper.SetEnabled(true);
            AppSettings.Instance.RunAtStartup = true;
            AppSettings.Instance.Save();
        }

        // Renders a single icon-font glyph onto a small transparent bitmap, sized and centered to
        // match the space WinForms reserves to the left of a ToolStripMenuItem's text (a plain
        // 16x16 covers it with a touch of breathing room). Theme.Ink keeps it legible against the
        // dark-mode menu background from Application.SetColorMode in Program.cs.
        private static Bitmap CreateMenuIcon(char glyph)
        {
            const int size = 16;
            var bitmap = new Bitmap(size, size);

            using var g = Graphics.FromImage(bitmap);
            using var brush = new SolidBrush(Theme.Ink);
            using var format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };

            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.DrawString(glyph.ToString(), Theme.IconFont, brush, new RectangleF(0, 0, size, size), format);

            return bitmap;
        }

        private void OnKeyboardModeChanged(bool enabled)
        {
            Debug.WriteLine($"OnKeyboardModeChanged enabled={enabled}");

            if (_overlay.InvokeRequired)
            {
                _overlay.BeginInvoke(new Action(() => HandleKeyboardModeChanged(enabled)));
            }
            else
            {
                HandleKeyboardModeChanged(enabled);
            }
        }

        private void HandleKeyboardModeChanged(bool enabled)
        {
            Debug.WriteLine($"HandleKeyboardModeChanged enabled={enabled}");

            if (enabled)
            {
                PositionOverlayOnActiveMonitor(_overlay);
            }
        }

        private static void PositionOverlayOnActiveMonitor(Form form)
        {
            var cursorPos = Cursor.Position;
            var activeScreen = Screen.FromPoint(cursorPos);
            var bounds = activeScreen.WorkingArea;

            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(
                bounds.X + (bounds.Width - form.Width) / 2,
                bounds.Y + (bounds.Height - form.Height) / 2
            );
        }

        private void OnSettingsClick(object? sender, EventArgs e)
        {
            using var form = new SettingsForm(_controllerPoller);
            form.ShowDialog();
        }

        private void OnRestartClick(object? sender, EventArgs e)
        {
            var exe = Application.ExecutablePath;

            try
            {
                Process.Start(exe);
            }
            catch
            {
            }

            ExitThread();
        }

        private void OnExitClick(object? sender, EventArgs e)
        {
            ExitThread();
        }

        protected override void ExitThreadCore()
        {
            _controllerPoller.KeyboardModeChanged -= OnKeyboardModeChanged;
            _controllerPoller.Stop();

            _overlay?.Close();

            _trayIcon.Visible = false;
            _trayIcon.Dispose();

            base.ExitThreadCore();
        }

        // ExitThreadCore already disposes these eagerly (so the tray icon vanishes immediately on
        // Exit, without waiting for the message loop to fully unwind) and Dispose() on an
        // already-disposed NotifyIcon/Form is a safe no-op, so this is a belt-and-suspenders
        // guarantee for any disposal path that doesn't go through ExitThreadCore first, rather
        // than something expected to normally run first.
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _trayIcon.Dispose();
                _overlay?.Dispose();
                _settingsIcon.Dispose();
                _restartIcon.Dispose();
                _exitIcon.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
