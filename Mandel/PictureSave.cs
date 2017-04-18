using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace Mandel
{
    public partial class PictureSave : Form
    {
        public bool rawRequired = false, cropMode = false;
        public int sfw = 1920, sfh = 1080;
        public long jpgQual = 90;
        public Guid saveFormat = ImageFormat.Jpeg.Guid;
        public PictureSave()
        {
            InitializeComponent();
        }
       
        private void sfWidth_TextChanged(object sender, EventArgs e)
        {
            try
            {
                sfw = (int)Convert.ToUInt32(sfWidth.Text);
                sfWidth.ForeColor = (sfw > 781 ? SystemColors.ControlText : Color.Firebrick);
            }
            catch (FormatException)
            { sfWidth.ForeColor = Color.Firebrick; }
        }

        private void button1_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void PictureSave_Load(object sender, EventArgs e)
        {
            sfWidth.Text = sfw.ToString();
            pwHeight.Text = sfh.ToString();
            if (saveFormat == ImageFormat.Jpeg.Guid)
            {
                label3.Show();
                numericUpDown1.Show();
            }
            else
            {
                label3.Hide();
                numericUpDown1.Hide();
                if (saveFormat == ImageFormat.Png.Guid)
                    checkBox1.Enabled = checkBox1.Checked = false;
            }
        }

        private void button2_Click(object sender, EventArgs e)
        {
            if (pwHeight.ForeColor != SystemColors.ControlText ||
                SystemColors.ControlText != sfWidth.ForeColor)
                MessageBox.Show("Some values are incorrect!");
            else
            {
                jpgQual = (long)numericUpDown1.Value;
                rawRequired = checkBox1.Checked;
                cropMode = checkBox2.Checked;
                Close();
            }
        }

        private void pwHeight_TextChanged(object sender, EventArgs e)
        {
            try
            {
                sfh = (int)Convert.ToUInt32(pwHeight.Text);
                pwHeight.ForeColor = (sfh > 479 ? SystemColors.ControlText : Color.Firebrick);
            }
            catch (FormatException)
            { sfWidth.ForeColor = Color.Firebrick; }
        }
    }
}
