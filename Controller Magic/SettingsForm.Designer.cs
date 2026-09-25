namespace ControllerMagic
{
    partial class SettingsForm
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            if (disposing)
            {
                _statusTimer.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        // Everything visible is built at runtime by SettingsForm.BuildLayout() and its helpers -
        // titlebar, status row, and cards are all custom-drawn, so there's nothing left for the
        // designer to lay out ahead of time.
        private void InitializeComponent()
        {
            this.SuspendLayout();
            //
            // SettingsForm
            //
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = Theme.Bg;
            this.ClientSize = new System.Drawing.Size(520, 400);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Name = "SettingsForm";
            this.Text = "Controller Magic Settings";
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.ResumeLayout(false);
        }

        #endregion
    }
}
