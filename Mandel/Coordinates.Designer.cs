namespace Mandel
{
    partial class Coordinates
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
            this.pixelStep = new System.Windows.Forms.TextBox();
            this.ulRe = new System.Windows.Forms.TextBox();
            this.ulIm = new System.Windows.Forms.TextBox();
            this.label1 = new System.Windows.Forms.Label();
            this.label2 = new System.Windows.Forms.Label();
            this.label3 = new System.Windows.Forms.Label();
            this.button1 = new System.Windows.Forms.Button();
            this.button2 = new System.Windows.Forms.Button();
            this.SuspendLayout();
            // 
            // pixelStep
            // 
            this.pixelStep.Location = new System.Drawing.Point(12, 119);
            this.pixelStep.MaxLength = 20;
            this.pixelStep.Name = "pixelStep";
            this.pixelStep.Size = new System.Drawing.Size(128, 22);
            this.pixelStep.TabIndex = 3;
            this.pixelStep.Text = "0";
            this.pixelStep.TextChanged += new System.EventHandler(this.pixelStep_TextChanged);
            // 
            // ulRe
            // 
            this.ulRe.Location = new System.Drawing.Point(12, 29);
            this.ulRe.MaxLength = 20;
            this.ulRe.Name = "ulRe";
            this.ulRe.Size = new System.Drawing.Size(128, 22);
            this.ulRe.TabIndex = 1;
            this.ulRe.Text = "0.6822871999174";
            this.ulRe.TextChanged += new System.EventHandler(this.ulRe_TextChanged);
            // 
            // ulIm
            // 
            this.ulIm.Location = new System.Drawing.Point(12, 74);
            this.ulIm.MaxLength = 20;
            this.ulIm.Name = "ulIm";
            this.ulIm.Size = new System.Drawing.Size(128, 22);
            this.ulIm.TabIndex = 2;
            this.ulIm.Text = "0";
            this.ulIm.TextChanged += new System.EventHandler(this.ulIm_TextChanged);
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(12, 9);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(93, 17);
            this.label1.TabIndex = 8;
            this.label1.Text = "Upper-left Re";
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(12, 54);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(89, 17);
            this.label2.TabIndex = 8;
            this.label2.Text = "Upper-left Im";
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.Location = new System.Drawing.Point(12, 99);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(37, 17);
            this.label3.TabIndex = 8;
            this.label3.Text = "Step";
            // 
            // button1
            // 
            this.button1.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.button1.Location = new System.Drawing.Point(12, 147);
            this.button1.Name = "button1";
            this.button1.Size = new System.Drawing.Size(60, 25);
            this.button1.TabIndex = 5;
            this.button1.Text = "Cancel";
            this.button1.UseVisualStyleBackColor = true;
            this.button1.Click += new System.EventHandler(this.button1_Click);
            // 
            // button2
            // 
            this.button2.DialogResult = System.Windows.Forms.DialogResult.OK;
            this.button2.Location = new System.Drawing.Point(80, 147);
            this.button2.Name = "button2";
            this.button2.Size = new System.Drawing.Size(60, 25);
            this.button2.TabIndex = 4;
            this.button2.Text = "OK";
            this.button2.UseVisualStyleBackColor = true;
            this.button2.Click += new System.EventHandler(this.button2_Click);
            // 
            // Coordinates
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(120F, 120F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
            this.ClientSize = new System.Drawing.Size(152, 180);
            this.Controls.Add(this.button2);
            this.Controls.Add(this.button1);
            this.Controls.Add(this.label3);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.pixelStep);
            this.Controls.Add(this.ulRe);
            this.Controls.Add(this.ulIm);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedToolWindow;
            this.Name = "Coordinates";
            this.Text = "Coordinates";
            this.Load += new System.EventHandler(this.Coordinates_Load);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.TextBox pixelStep;
        private System.Windows.Forms.TextBox ulRe;
        private System.Windows.Forms.TextBox ulIm;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.Button button1;
        private System.Windows.Forms.Button button2;
    }
}