using System.Diagnostics;

namespace ControllerMagic
{
    internal class TrayApplicationContext : ApplicationContext
    {
        private readonly NotifyIcon _trayIcon;
        private readonly ControllerPoller _controllerPoller;
        private readonly KeyboardOverlayForm _overlay;

        public TrayApplicationContext()
        {
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

            _controllerPoller = new ControllerPoller();
            _controllerPoller.KeyboardModeChanged += OnKeyboardModeChanged;
            _controllerPoller.Start();

            _overlay = new KeyboardOverlayForm(_controllerPoller);
            _overlay.Show();
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

        private void PositionOverlayOnActiveMonitor(Form form)
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
            using var form = new SettingsForm();
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
    }
}
