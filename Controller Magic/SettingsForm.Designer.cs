namespace ControllerMagic
{
    partial class SettingsForm
    {
        private System.ComponentModel.IContainer components = null;

        private System.Windows.Forms.Button closeButton;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        // Everything below the close button is laid out at runtime by SettingsForm.BuildLayout()
        // instead of being hand-positioned here, so adding/removing a setting row doesn't require
        // touching this file or recomputing every Location below it.
        private void InitializeComponent()
        {
            this.closeButton = new System.Windows.Forms.Button();
            this.SuspendLayout();
            //
            // closeButton
            //
            this.closeButton.Anchor = (System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right);
            this.closeButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.closeButton.FlatAppearance.BorderSize = 0;
            this.closeButton.BackColor = System.Drawing.Color.FromArgb(1, 0, 0, 0);
            this.closeButton.ForeColor = System.Drawing.Color.Lime;
            this.closeButton.Location = new System.Drawing.Point(440, 8);
            this.closeButton.Name = "closeButton";
            this.closeButton.Size = new System.Drawing.Size(24, 24);
            this.closeButton.TabIndex = 100;
            this.closeButton.Text = "X";
            this.closeButton.UseVisualStyleBackColor = false;
            this.closeButton.Click += new System.EventHandler(this.CloseButton_Click);
            //
            // SettingsForm
            //
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.Black;
            this.Opacity = 0.8D;
            this.ClientSize = new System.Drawing.Size(480, 400);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None; // no Windows bar
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Name = "SettingsForm";
            this.Text = "Controller Magic Settings";
            this.MouseDown += new System.Windows.Forms.MouseEventHandler(this.SettingsForm_MouseDown);
            this.MouseMove += new System.Windows.Forms.MouseEventHandler(this.SettingsForm_MouseMove);
            this.MouseUp += new System.Windows.Forms.MouseEventHandler(this.SettingsForm_MouseUp);
            this.Controls.Add(this.closeButton);
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.ResumeLayout(false);
        }

        #endregion
    }
}
