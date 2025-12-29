using System.Diagnostics;

namespace ControllerMagic
{
    internal class TrayApplicationContext : ApplicationContext
    {
        private readonly NotifyIcon _trayIcon;
        private readonly ControllerPoller _controllerPoller;
        private readonly KeyboardOverlayForm _overlay; // NEW
        public TrayApplicationContext()
        {
            _trayIcon = new NotifyIcon
            {
                Icon = Controller_Magic.Properties.Resources.Controller,
                Text = "Controller Magic",
                Visible = true
            };

            var menu = new ContextMenuStrip();

            var settingsItem = new ToolStripMenuItem("Settings...", null, OnSettingsClick);
            var bigPictureItem = new ToolStripMenuItem("Big Picture Mode");
            bigPictureItem.Enabled = false; // placeholder for future mode

            var restartItem = new ToolStripMenuItem("Restart", null, OnRestartClick);
            var exitItem = new ToolStripMenuItem("Exit", null, OnExitClick);

            menu.Items.Add(settingsItem);
            menu.Items.Add(bigPictureItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(restartItem);
            menu.Items.Add(exitItem);

            _trayIcon.ContextMenuStrip = menu;

            _controllerPoller = new ControllerPoller();
            _controllerPoller.Start();

            _overlay = new KeyboardOverlayForm(_controllerPoller);
            _overlay.Show();
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
            catch { /* handle errors later */ }

            ExitThread();
        }

        private void OnExitClick(object? sender, EventArgs e)
        {
            ExitThread();
        }

        protected override void ExitThreadCore()
        {
            _controllerPoller.Stop();
            _overlay?.Close();
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            base.ExitThreadCore();
        }
    }
}
