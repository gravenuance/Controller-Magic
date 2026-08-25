using System.Diagnostics;

namespace ControllerMagic
{
    internal sealed class TrayApplicationContext : ApplicationContext
    {
        private readonly NotifyIcon _trayIcon;
        private readonly ControllerPoller _controllerPoller;
        private readonly KeyboardOverlayForm _overlay;

        public TrayApplicationContext()
        {
            StartupHelper.EnsureMigrated();

            _trayIcon = new NotifyIcon
            {
                Icon = Properties.Resources.Controller,
                Text = "Controller Magic",
                Visible = true
            };

            var menu = new ContextMenuStrip();

            var settingsItem = new ToolStripMenuItem("Settings...", null, OnSettingsClick);
            var restartItem = new ToolStripMenuItem("Restart", null, OnRestartClick);
            var exitItem = new ToolStripMenuItem("Exit", null, OnExitClick);

            menu.Items.Add(settingsItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(restartItem);
            menu.Items.Add(exitItem);

            _trayIcon.ContextMenuStrip = menu;

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
            }
            base.Dispose(disposing);
        }
    }
}
