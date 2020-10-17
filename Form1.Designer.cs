namespace FetchRig3
{
    partial class Form1
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
            if (disposing && (components != null))
            {
                components.Dispose();
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
            this.mergedImgBox0 = new Emgu.CV.UI.ImageBox();
            this.mergedImgBox1 = new Emgu.CV.UI.ImageBox();
            ((System.ComponentModel.ISupportInitialize)(this.mergedImgBox0)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.mergedImgBox1)).BeginInit();
            this.SuspendLayout();
            // 
            // mergedImgBox0
            // 
            this.mergedImgBox0.FunctionalMode = Emgu.CV.UI.ImageBox.FunctionalModeOption.PanAndZoom;
            this.mergedImgBox0.Location = new System.Drawing.Point(12, 12);
            this.mergedImgBox0.Name = "mergedImgBox0";
            this.mergedImgBox0.Size = new System.Drawing.Size(802, 1100);
            this.mergedImgBox0.TabIndex = 2;
            this.mergedImgBox0.TabStop = false;
            // 
            // mergedImgBox1
            // 
            this.mergedImgBox1.FunctionalMode = Emgu.CV.UI.ImageBox.FunctionalModeOption.PanAndZoom;
            this.mergedImgBox1.Location = new System.Drawing.Point(820, 12);
            this.mergedImgBox1.Name = "mergedImgBox1";
            this.mergedImgBox1.Size = new System.Drawing.Size(802, 1100);
            this.mergedImgBox1.TabIndex = 3;
            this.mergedImgBox1.TabStop = false;
            // 
            // Form1
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1639, 1122);
            this.Controls.Add(this.mergedImgBox1);
            this.Controls.Add(this.mergedImgBox0);
            this.Name = "Form1";
            this.Text = "Form1";
            ((System.ComponentModel.ISupportInitialize)(this.mergedImgBox0)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.mergedImgBox1)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        private Emgu.CV.UI.ImageBox mergedImgBox0;
        private Emgu.CV.UI.ImageBox mergedImgBox1;
    }
}

