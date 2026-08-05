namespace ControllerMagic
{
    partial class KeyboardOverlayForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (components != null)
                    components.Dispose();

                _timer.Dispose();
                _textBrush.Dispose();
                _hotBrush.Dispose();
                _normalBrush.Dispose();
                _pen.Dispose();
                _tileFont.Dispose();
                _legendFont.Dispose();
                _centerFormat.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.StartPosition = System.Windows.Forms.FormStartPosition.Manual;
            this.ClientSize = new System.Drawing.Size(800, 450);
            this.Text = "KeyboardOverlayForm";
        }

        #endregion
    }
}