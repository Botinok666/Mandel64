using System;
using System.Data;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace Mandel
{
    public struct SaveParams
    {
        public bool rawRequired;
        public Size size;
        public byte tileX, tileY;
        public long quality;
        public float overlap;
        public string filename;
        public Guid guid;
    }
    public partial class PictureSave : Form
    {
        private DataTable pageSize = new DataTable();
        private DataTable pageDPI = new DataTable();
        public SaveParams sp;
        Guid[] saveFormat = new Guid[] { ImageFormat.Jpeg.Guid, ImageFormat.Tiff.Guid,
            ImageFormat.Png.Guid };
        SaveFileDialog saveFileDialog = new SaveFileDialog();

        public PictureSave()
        {
            InitializeComponent();
            pageSize.Columns.Add("Size", typeof(string));
            pageSize.Columns.Add("Width", typeof(float));
            pageSize.Columns.Add("Height", typeof(float));
            pageSize.Rows.Add(new object[] { "A4 Portrait", 8.27f, 11.69f });
            pageSize.Rows.Add(new object[] { "A4 Landscape", 11.69f, 8.27f });
            pageSize.Rows.Add(new object[] { "A3 Portrait", 11.69f, 16.53f });
            pageSize.Rows.Add(new object[] { "A3 Landscape", 16.53f, 11.69f });
            comboBox1.DataSource = pageSize;
            comboBox1.DisplayMember = "Size";
            pageDPI.Columns.Add("DPI", typeof(float));
            pageDPI.Rows.Add(new object[] { 300f });
            pageDPI.Rows.Add(new object[] { 200f });
            pageDPI.Rows.Add(new object[] { 150f });
            pageDPI.Rows.Add(new object[] { 100f });
            comboBox2.DataSource = pageDPI;
            comboBox2.DisplayMember = "DPI";
        }
       
        private void sfWidth_TextChanged(object sender, EventArgs e)
        {
            try
            {
                sp.size.Width = (int)Convert.ToUInt32(sfWidth.Text);
                sfWidth.ForeColor = (sp.size.Width > 781 ? SystemColors.ControlText : Color.Firebrick);
                if (sp.size.Width > 781)
                    (Owner as Main).SaveFrameChanged(sp.size, DialogResult.Yes);
            }
            catch (FormatException)
            {
                sfWidth.ForeColor = Color.Firebrick;
            }
        }

        private void pwHeight_TextChanged(object sender, EventArgs e)
        {
            try
            {
                sp.size.Height = (int)Convert.ToUInt32(pwHeight.Text);
                pwHeight.ForeColor = (sp.size.Height > 479 ? SystemColors.ControlText : Color.Firebrick);
                if (sp.size.Height > 479)
                    (Owner as Main).SaveFrameChanged(sp.size, DialogResult.Yes);
            }
            catch (FormatException)
            {
                sfWidth.ForeColor = Color.Firebrick;
            }
        }

        private void PictureSave_FormClosing(object sender, FormClosingEventArgs e)
        {
        }

        private void button1_Click(object sender, EventArgs e)
        {
            (Owner as Main).SaveFrameChanged(new Size(0, 0), DialogResult.Cancel);
            Close();
        }

        private void PictureSave_Load(object sender, EventArgs e)
        {
            if (sp.rawRequired)
            {
                if (MessageBox.Show("Make images from binary data?", "Binary data found", 
                    MessageBoxButtons.YesNo) == DialogResult.Yes)
                {
                    saveFileDialog.FileName = DateTime.Now.ToString("ddMMM_HH-mm");
                    saveFileDialog.Filter = "JPEG (*.jpg)|*.jpg|TIFF (*.tiff)|*.tiff|PNG (*.png)|*.png";
                    if (saveFileDialog.ShowDialog() == DialogResult.OK)
                    {
                        sp.guid = saveFormat[saveFileDialog.FilterIndex - 1];
                        sp.filename = saveFileDialog.FileName;
                        (Owner as Main).PictureSaveAsync(sp);
                    }
                }
                Close();
            }
        }

        private void numericUpDown2_ValueChanged(object sender, EventArgs e)
        {
            float pw = (float)pageSize.Rows[comboBox1.SelectedIndex]["Width"] * (float)numericUpDown2.Value;
            float ph = (float)pageSize.Rows[comboBox1.SelectedIndex]["Height"] * (float)numericUpDown3.Value;
            label8.Text = string.Format("Overall size: {0:F2}*{1:F2}m", pw * 2.54e-2f, ph * 2.54e-2f);
            float dpi = (float)pageDPI.Rows[comboBox2.SelectedIndex]["DPI"];
            (Owner as Main).SaveFrameChanged(new Size((int)(pw * dpi), (int)(ph * dpi)), DialogResult.Yes);
        }

        private void button2_Click(object sender, EventArgs e)
        {
            if (pwHeight.ForeColor == Color.Firebrick || Color.Firebrick == sfWidth.ForeColor)
                MessageBox.Show("Some values are incorrect!");
            else
            {
                sp.quality = (long)numericUpDown1.Value;
                sp.rawRequired = checkBox1.Checked;
                if (tabControl1.SelectedTab.Equals(tabPage1))
                {
                    sp.tileX = sp.tileY = 1;
                    sp.overlap = 0;
                }
                else
                {
                    float pw = (float)pageSize.Rows[comboBox1.SelectedIndex]["Width"];
                    float ph = (float)pageSize.Rows[comboBox1.SelectedIndex]["Height"];
                    float dpi = (float)pageDPI.Rows[comboBox2.SelectedIndex]["DPI"];
                    sp.size = new Size((int)(pw * dpi), (int)(ph * dpi));
                    sp.tileX = (byte)numericUpDown2.Value;
                    sp.tileY = (byte)numericUpDown3.Value;
                    sp.overlap = (float)numericUpDown4.Value / 100f;
                }
                saveFileDialog.FileName = DateTime.Now.ToString("ddMMM_HH-mm");
                saveFileDialog.Filter = "JPEG (*.jpg)|*.jpg|TIFF (*.tiff)|*.tiff|PNG (*.png)|*.png";
                if (saveFileDialog.ShowDialog() == DialogResult.OK)
                {
                    sp.guid = saveFormat[saveFileDialog.FilterIndex - 1];
                    sp.filename = saveFileDialog.FileName;
                    (Owner as Main).SaveFrameChanged(new Size(0, 0), DialogResult.OK);
                    (Owner as Main).PictureSaveAsync(sp);
                }
                else
                    (Owner as Main).SaveFrameChanged(new Size(0, 0), DialogResult.Cancel);
                Close();
            }
        }
        public DialogResult LastResult
        { get; private set; } 
    }
}
