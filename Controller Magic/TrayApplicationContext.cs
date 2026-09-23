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
        private readonly ResourceUsageMonitor _resourceMonitor;

        public TrayApplicationContext()
        {
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

            _controllerPoller = new ControllerPoller();
            _controllerPoller.KeyboardModeChanged += OnKeyboardModeChanged;
            _controllerPoller.PassthroughNoticeRaised += OnPassthroughNotice;
            _controllerPoller.Start();

            _overlay = new KeyboardOverlayForm(_controllerPoller);
            // Defense in depth alongside KeyboardOverlayForm's own CreateParams fix: even with
            // this window fully click-through and colour-key transparent from frame one, there's
            // no reason to leave it sitting at its default (0,0) location - the top-left corner of
            // the primary monitor - before keyboard mode ever repositions it.
            PositionOverlayOnActiveMonitor(_overlay);
            _overlay.Show();

            _resourceMonitor = new ResourceUsageMonitor(TimeSpan.FromMinutes(10));

            // Startup-task housekeeping (migrating a legacy Run-key install, defaulting startup to
            // on for a first-ever run) spawns schtasks.exe and can briefly block on a UAC prompt -
            // run it in the background instead of delaying the tray icon's appearance on it.
            _ = InitializeStartupAsync();
        }

        // Runs EnsureMigratedAsync every launch (a no-op once there's no legacy Run-key value left
        // to migrate), then - once, ever, the first time the app starts - defaults startup to on,
        // no prompt. SetEnabledAsync already handles the unelevated task attempt, the UAC-elevated
        // retry if that's denied, and the Run-key fallback if elevation is declined, so this can
        // still surface a UAC prompt on locked-down machines; it just isn't an app-level dialog
        // asking permission first.
        private static async Task InitializeStartupAsync()
        {
            await StartupHelper.EnsureMigratedAsync().ConfigureAwait(false);

            if (AppSettings.Instance.HasInitializedStartup)
                return;

            AppSettings.Instance.HasInitializedStartup = true;
            await StartupHelper.SetEnabledAsync(true).ConfigureAwait(false);
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
            AppLog.Default.Info($"Keyboard mode {(enabled ? "enabled" : "disabled")}");

            if (_overlay.InvokeRequired)
            {
                _overlay.BeginInvoke(new Action(() => HandleKeyboardModeChanged(enabled)));
            }
            else
            {
                HandleKeyboardModeChanged(enabled);
            }
        }

        private void OnPassthroughNotice(PassthroughNotice notice)
        {
            string text = notice switch
            {
                PassthroughNotice.ReconnectToFinishHiding => "Turn your controller off and on to finish hiding it.",
                PassthroughNotice.TurnedOffBySafetyCutoff => "Use HidHide was turned off: too many window handles in use.",
                _ => throw new ArgumentOutOfRangeException(nameof(notice), notice, null),
            };

            _overlay.BeginInvoke(() => _trayIcon.ShowBalloonTip(10_000, "Controller Magic", text, ToolTipIcon.Info));
        }

        private void HandleKeyboardModeChanged(bool enabled)
        {
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
            // Diagnostic pair for the same handle-exhaustion investigation as
            // GamepadPassthroughController.ApplyTransition - if this dialog itself is the source,
            // repeated open/close cycles should show it. Note: if a handler thrown inside
            // ShowDialog() is caught by WinForms' own message loop (Application.ThreadException)
            // rather than propagating out, ShowDialog() never returns and the "after" snapshot
            // for that specific occurrence won't appear - that gap is itself a useful signal.
            ResourceUsageMonitor.LogSnapshot("before-settings-open");
            using var form = new SettingsForm(_controllerPoller);
            form.ShowDialog();
            ResourceUsageMonitor.LogSnapshot("after-settings-close");
        }

        private void OnRestartClick(object? sender, EventArgs e)
        {
            var exe = Application.ExecutablePath;

            try
            {
                Process.Start(exe);
            }
            catch (Exception ex)
            {
                AppLog.Default.Warning($"Failed to relaunch {exe} for restart", ex);
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
            _controllerPoller.PassthroughNoticeRaised -= OnPassthroughNotice;
            _controllerPoller.Stop();
            _controllerPoller.Dispose();

            _resourceMonitor.Dispose();

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
                _resourceMonitor.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
