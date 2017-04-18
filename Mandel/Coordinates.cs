using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Mandel
{
    public partial class Coordinates : Form
    {
        public Double re = 0, im = 0, step = 0;
        public Coordinates()
        {
            InitializeComponent();
        }

        private void ulRe_TextChanged(object sender, EventArgs e)
        {
            try
            {
                Convert.ToDouble(ulRe.Text);
                ulRe.ForeColor = SystemColors.WindowText;
            }
            catch (FormatException)
            { ulRe.ForeColor = Color.Firebrick; }
        }

        private void ulIm_TextChanged(object sender, EventArgs e)
        {
            try
            {
                Convert.ToDouble(ulIm.Text);
                ulIm.ForeColor = SystemColors.WindowText;
            }
            catch (FormatException)
            { ulIm.ForeColor = Color.Firebrick; }
        }

        private void pixelStep_TextChanged(object sender, EventArgs e)
        {
            try
            {
                Convert.ToDouble(pixelStep.Text);
                pixelStep.ForeColor = SystemColors.WindowText;
            }
            catch (FormatException)
            { pixelStep.ForeColor = Color.Firebrick; }
        }

        private void button2_Click(object sender, EventArgs e)
        {
            if (ulRe.ForeColor == SystemColors.WindowText && ulIm.ForeColor == SystemColors.WindowText &&
                pixelStep.ForeColor == SystemColors.WindowText)
            {
                re = Convert.ToDouble(ulRe.Text);
                im = Convert.ToDouble(ulIm.Text);
                step = Convert.ToDouble(pixelStep.Text);
                Close();
            }
            else
            {
                MessageBox.Show("Some values are incorrect!", "Error");
            }
        }

        private void button1_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void Coordinates_Load(object sender, EventArgs e)
        {
            ulRe.Text = re.ToString();
            ulIm.Text = im.ToString();
            pixelStep.Text = step.ToString("E4");
        }

    }
}
