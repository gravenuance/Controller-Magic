using System.Windows.Forms;

namespace ControllerMagic
{
    partial class SettingsForm
    {
        private System.ComponentModel.IContainer components = null;

        private CheckBox runAtStartupCheckBox;
        private Label deadZoneLabel;
        private TrackBar deadZoneTrackBar;
        private Label scrollDeadZoneLabel;
        private TrackBar scrollDeadZoneTrackBar;
        private Label keyboardDeadZoneLabel;
        private TrackBar keyboardDeadZoneTrackBar;
        private Label sensitivityLabel;
        private TrackBar sensitivityTrackBar;
        private Button closeButton;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        private void InitializeComponent()
        {
            this.runAtStartupCheckBox = new System.Windows.Forms.CheckBox();
            this.deadZoneLabel = new System.Windows.Forms.Label();
            this.deadZoneTrackBar = new System.Windows.Forms.TrackBar();
            this.scrollDeadZoneLabel = new System.Windows.Forms.Label();
            this.scrollDeadZoneTrackBar = new System.Windows.Forms.TrackBar();
            this.keyboardDeadZoneLabel = new System.Windows.Forms.Label();
            this.keyboardDeadZoneTrackBar = new System.Windows.Forms.TrackBar();
            this.sensitivityLabel = new System.Windows.Forms.Label();
            this.sensitivityTrackBar = new System.Windows.Forms.TrackBar();
            this.closeButton = new System.Windows.Forms.Button();
            ((System.ComponentModel.ISupportInitialize)(this.deadZoneTrackBar)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.scrollDeadZoneTrackBar)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.keyboardDeadZoneTrackBar)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.sensitivityTrackBar)).BeginInit();
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
            // runAtStartupCheckBox
            this.runAtStartupCheckBox.AutoSize = false;
            this.runAtStartupCheckBox.Appearance = System.Windows.Forms.Appearance.Button; // toggle style[web:383][web:397]
            this.runAtStartupCheckBox.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.runAtStartupCheckBox.FlatAppearance.BorderSize = 1;
            this.runAtStartupCheckBox.FlatAppearance.BorderColor = System.Drawing.Color.DimGray;
            this.runAtStartupCheckBox.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;

            this.runAtStartupCheckBox.ForeColor = System.Drawing.Color.White;
            this.runAtStartupCheckBox.Location = new System.Drawing.Point(20, 20);
            this.runAtStartupCheckBox.Size = new System.Drawing.Size(120, 28);
            this.runAtStartupCheckBox.Name = "runAtStartupCheckBox";
            this.runAtStartupCheckBox.TabIndex = 0;
            this.runAtStartupCheckBox.UseVisualStyleBackColor = false;
            this.runAtStartupCheckBox.CheckedChanged += new System.EventHandler(this.RunAtStartupCheckBox_CheckedChanged);
            // 
            // deadZoneLabel
            // 
            this.deadZoneLabel.AutoSize = true;
            this.deadZoneLabel.ForeColor = System.Drawing.Color.Lime;
            this.deadZoneLabel.Location = new System.Drawing.Point(20, 60);
            this.deadZoneLabel.Name = "deadZoneLabel";
            this.deadZoneLabel.Size = new System.Drawing.Size(136, 15);
            this.deadZoneLabel.TabIndex = 1;
            this.deadZoneLabel.Text = "Stick deadzone (move)";
            // 
            // deadZoneTrackBar
            // 
            this.deadZoneTrackBar.Location = new System.Drawing.Point(20, 80);
            this.deadZoneTrackBar.Minimum = 0;
            this.deadZoneTrackBar.Maximum = 10000;
            this.deadZoneTrackBar.TickFrequency = 1000;
            this.deadZoneTrackBar.Name = "deadZoneTrackBar";
            this.deadZoneTrackBar.Size = new System.Drawing.Size(430, 45);
            this.deadZoneTrackBar.TabIndex = 2;
            this.deadZoneTrackBar.Scroll += new System.EventHandler(this.DeadZoneTrackBar_Scroll);
            // 
            // scrollDeadZoneLabel
            // 
            this.scrollDeadZoneLabel.AutoSize = true;
            this.scrollDeadZoneLabel.ForeColor = System.Drawing.Color.Lime;
            this.scrollDeadZoneLabel.Location = new System.Drawing.Point(20, 130);
            this.scrollDeadZoneLabel.Name = "scrollDeadZoneLabel";
            this.scrollDeadZoneLabel.Size = new System.Drawing.Size(144, 15);
            this.scrollDeadZoneLabel.TabIndex = 3;
            this.scrollDeadZoneLabel.Text = "Stick deadzone (scroll)";
            // 
            // scrollDeadZoneTrackBar
            // 
            this.scrollDeadZoneTrackBar.Location = new System.Drawing.Point(20, 150);
            this.scrollDeadZoneTrackBar.Minimum = 0;
            this.scrollDeadZoneTrackBar.Maximum = 10000;
            this.scrollDeadZoneTrackBar.TickFrequency = 1000;
            this.scrollDeadZoneTrackBar.Name = "scrollDeadZoneTrackBar";
            this.scrollDeadZoneTrackBar.Size = new System.Drawing.Size(430, 45);
            this.scrollDeadZoneTrackBar.TabIndex = 4;
            this.scrollDeadZoneTrackBar.Scroll += new System.EventHandler(this.ScrollDeadZoneTrackBar_Scroll);
            // 
            // keyboardDeadZoneLabel
            // 
            this.keyboardDeadZoneLabel.AutoSize = true;
            this.keyboardDeadZoneLabel.ForeColor = System.Drawing.Color.Lime;
            this.keyboardDeadZoneLabel.Location = new System.Drawing.Point(20, 195);
            this.keyboardDeadZoneLabel.Name = "keyboardDeadZoneLabel";
            this.keyboardDeadZoneLabel.Size = new System.Drawing.Size(163, 15);
            this.keyboardDeadZoneLabel.TabIndex = 5;
            this.keyboardDeadZoneLabel.Text = "Stick deadzone (keyboard)";
            // 
            // keyboardDeadZoneTrackBar
            // 
            this.keyboardDeadZoneTrackBar.Location = new System.Drawing.Point(20, 215);
            this.keyboardDeadZoneTrackBar.Minimum = 0;
            this.keyboardDeadZoneTrackBar.Maximum = 10000;
            this.keyboardDeadZoneTrackBar.TickFrequency = 1000;
            this.keyboardDeadZoneTrackBar.Name = "keyboardDeadZoneTrackBar";
            this.keyboardDeadZoneTrackBar.Size = new System.Drawing.Size(430, 45);
            this.keyboardDeadZoneTrackBar.TabIndex = 6;
            this.keyboardDeadZoneTrackBar.Scroll += new System.EventHandler(this.KeyboardDeadZoneTrackBar_Scroll);
            // 
            // sensitivityLabel
            // 
            this.sensitivityLabel.AutoSize = true;
            this.sensitivityLabel.ForeColor = System.Drawing.Color.Lime;
            this.sensitivityLabel.Location = new System.Drawing.Point(20, 260);
            this.sensitivityLabel.Name = "sensitivityLabel";
            this.sensitivityLabel.Size = new System.Drawing.Size(143, 15);
            this.sensitivityLabel.TabIndex = 7;
            this.sensitivityLabel.Text = "Stick sensitivity (x1.00)";
            // 
            // sensitivityTrackBar
            // 
            this.sensitivityTrackBar.Location = new System.Drawing.Point(20, 280);
            this.sensitivityTrackBar.Minimum = 1;
            this.sensitivityTrackBar.Maximum = 5;
            this.sensitivityTrackBar.TickFrequency = 1;
            this.sensitivityTrackBar.Name = "sensitivityTrackBar";
            this.sensitivityTrackBar.Size = new System.Drawing.Size(430, 45);
            this.sensitivityTrackBar.TabIndex = 8;
            this.sensitivityTrackBar.Scroll += new System.EventHandler(this.SensitivityTrackBar_Scroll);
            // 
            // SettingsForm
            // 
            // SettingsForm
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.Black;
            this.Opacity = 0.8D;
            this.ClientSize = new System.Drawing.Size(480, 350);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None; // no Windows bar[web:352]
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Name = "SettingsForm";
            this.Text = "Controller Magic Settings";
            this.MouseDown += new System.Windows.Forms.MouseEventHandler(this.SettingsForm_MouseDown);
            this.MouseMove += new System.Windows.Forms.MouseEventHandler(this.SettingsForm_MouseMove);
            this.MouseUp += new System.Windows.Forms.MouseEventHandler(this.SettingsForm_MouseUp);
            this.Controls.Add(this.closeButton);
            this.Controls.Add(this.runAtStartupCheckBox);
            this.Controls.Add(this.deadZoneLabel);
            this.Controls.Add(this.deadZoneTrackBar);
            this.Controls.Add(this.scrollDeadZoneLabel);
            this.Controls.Add(this.scrollDeadZoneTrackBar);
            this.Controls.Add(this.keyboardDeadZoneLabel);
            this.Controls.Add(this.keyboardDeadZoneTrackBar);
            this.Controls.Add(this.sensitivityLabel);
            this.Controls.Add(this.sensitivityTrackBar);
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "SettingsForm";
            this.Text = "Controller Magic Settings";
            ((System.ComponentModel.ISupportInitialize)(this.deadZoneTrackBar)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.scrollDeadZoneTrackBar)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.keyboardDeadZoneTrackBar)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.sensitivityTrackBar)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        #endregion
    }
}
