namespace ControllerMagic
{
    public partial class SettingsForm : Form
    {
        public SettingsForm()
        {
            InitializeComponent();
            this.Icon = ControllerMagic.Properties.Resources.Controller;
            LoadSettingsIntoUI();
        }

        private void LoadSettingsIntoUI()
        {
            bool startupEnabled = StartupHelper.IsEnabled();
            AppSettings.Instance.RunAtStartup = startupEnabled;

            runAtStartupCheckBox.Checked = startupEnabled;
            UpdateStartupSwitchVisual();

            deadZoneTrackBar.Value = AppSettings.Instance.StickDeadZone;
            scrollDeadZoneTrackBar.Value = AppSettings.Instance.ScrollDeadZone;
            keyboardDeadZoneTrackBar.Value = AppSettings.Instance.KeyboardDeadZone;
            sensitivityTrackBar.Value = (int)(AppSettings.Instance.StickSensitivity * 100);
            UpdateSensitivityLabel();
        }

        private void UpdateStartupSwitchVisual()
        {
            if (runAtStartupCheckBox.Checked)
            {
                runAtStartupCheckBox.Text = "Startup: ON";
                runAtStartupCheckBox.BackColor =
                    System.Drawing.Color.FromArgb(120, 46, 204, 113);
            }
            else
            {
                runAtStartupCheckBox.Text = "Startup: OFF";
                runAtStartupCheckBox.BackColor =
                    System.Drawing.Color.FromArgb(120, 149, 165, 166);
            }
        }



        private void runAtStartupCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            bool enabled = runAtStartupCheckBox.Checked;

            // Update registry and settings
            StartupHelper.SetEnabled(enabled);
            AppSettings.Instance.RunAtStartup = enabled;
            AppSettings.Instance.Save();

            UpdateStartupSwitchVisual();
        }

        private void deadZoneTrackBar_Scroll(object sender, EventArgs e)
        {
            AppSettings.Instance.StickDeadZone = deadZoneTrackBar.Value;
            AppSettings.Instance.Save();
        }

        private void scrollDeadZoneTrackBar_Scroll(object sender, EventArgs e)
        {
            AppSettings.Instance.ScrollDeadZone = scrollDeadZoneTrackBar.Value;
            AppSettings.Instance.Save();
        }

        private void keyboardDeadZoneTrackBar_Scroll(object sender, EventArgs e)
        {
            AppSettings.Instance.KeyboardDeadZone = keyboardDeadZoneTrackBar.Value;
            AppSettings.Instance.Save();
        }

        private void sensitivityTrackBar_Scroll(object sender, EventArgs e)
        {
            AppSettings.Instance.StickSensitivity = sensitivityTrackBar.Value / 100f;
            AppSettings.Instance.Save();
            UpdateSensitivityLabel();
        }
        private void UpdateSensitivityLabel()
        {
            float factor = sensitivityTrackBar.Value / 100f;
            sensitivityLabel.Text = $"Stick sensitivity (x{factor:0.00})";
        }


        private bool _dragging;
        private System.Drawing.Point _dragStart;

        private void SettingsForm_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _dragging = true;
                _dragStart = e.Location;
            }
        }

        private void SettingsForm_MouseMove(object sender, MouseEventArgs e)
        {
            if (_dragging)
            {
                var screenPos = PointToScreen(e.Location);
                Location = new System.Drawing.Point(screenPos.X - _dragStart.X, screenPos.Y - _dragStart.Y);
            }
        }

        private void SettingsForm_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
                _dragging = false;
        }
        private void closeButton_Click(object sender, EventArgs e)
        {
            Close();
        }
    }
}
