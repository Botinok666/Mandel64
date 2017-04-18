using System;
//using System.Collections.Generic;
//using System.ComponentModel;
using System.Linq;
using System.IO;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Diagnostics;
using System.Numerics;

namespace Mandel
{
    public partial class Form1 : Form
    {
        Bitmap filled_gradient = new Bitmap(200, 1), g_arrow = new Bitmap(5, 20);
        Bitmap image = new Bitmap(64, 48);
        bool form_resizing = false, prevStateMinimized = false;
        int selected_grad = -1, pgrad_count = 6, egrad_count = 4, prev_iterations = 4;
        float nextposition = -1;
        float[,] pixel_iterations = new float[2, 2], storePrev = null;
        double re = -2, im = 1.15, step = 26.0 / (1 << 16);
        double oldRe = -2, oldIm = 1.15, oldStep = 2e-6;
        Point mouse_down = new Point(-1, 0), mouse_move = new Point(-1, 0), lr = new Point(0, 0);
        Point shiftGrad = new Point(0, 0);

        ContextMenu pbox = new ContextMenu(), gbox = new ContextMenu(), fileopts = new ContextMenu();
        MenuItem[] aaMenu = new MenuItem[3] { new MenuItem("None"), new MenuItem("2xHRAA"), new MenuItem("4xSSAA") },
            renderMenu = new MenuItem[2] { new MenuItem("Weighted"), new MenuItem("Logarithmic") },
            pboxMenu = new MenuItem[4] { new MenuItem("Set standard scale"), new MenuItem("Show coordinates"),
                new MenuItem("Manually set coordinates"), new MenuItem("Drag picture by mouse")},
            gboxMenu = new MenuItem[2] { new MenuItem("Change color"), new MenuItem("Delete") },
            fileoptsMenu = new MenuItem[3] { new MenuItem("Open coordinates file"),
                new MenuItem("Save coordinates file"), new MenuItem("Save picture") };
        ColorDialog choosecolor = new ColorDialog();
        PictureSave psDiag = new PictureSave();
        Guid[] saveFormat = new Guid[3] { ImageFormat.Jpeg.Guid, ImageFormat.Tiff.Guid, ImageFormat.Png.Guid };
        ToolTip coordinates = new ToolTip();
        Stopwatch wtime = new Stopwatch();
        const float settingsVersion = 1.04f;

        Action _cancelWork = null;
        int _progress = 0;
        
        struct Gradient 
        {
            public Color color;
            public float position;
            public Gradient(Color Color, float Position)
            {
                color = Color;
                position = Position;
            }
        }
        struct PairGradient
        {
            public Vector3 left, diff;
            public float leftBorder;
            public float weight;
        }
        struct AA
        {
            public const int None = 0;
            public const int SS4x = 2;
            public const int HR2x = 1;
        }
        Gradient[] pgrad = new Gradient[16], egrad = new Gradient[16];
        Brush arrowBrush;
        Pen arrowPen = new Pen(Color.Orchid);
        static Vector<double> dummy;
        public Form1()
        {
            InitializeComponent();
            dummy = Vector<double>.One;
            Process cp = Process.GetCurrentProcess();
            cp.PriorityClass = ProcessPriorityClass.BelowNormal;
        }

        #region Picture_context_menu
        private void PboxUpdate(object sender, EventArgs e)
        {
            //Perform only rendering of black pixels if iterations count raised
            if (pixelIterations.ForeColor == SystemColors.WindowText)
            {
                int i = Convert.ToInt32(pixelIterations.Text);
                Recalculate(image.Width, image.Height,
                    i >= prev_iterations, i > prev_iterations);
            }
        }
        private void OpenCoordinatesDiag(object sender, EventArgs e)
        {
            Coordinates open_diag = new Coordinates();
            open_diag.re = re;
            open_diag.im = im;
            open_diag.step = step;
            if (open_diag.ShowDialog() == DialogResult.OK)
            {
                re = open_diag.re;
                im = open_diag.im;
                step = open_diag.step;
                Recalculate(image.Width, image.Height);
            }
            open_diag.Dispose();
        }
        private void SwitchCoordinates(object sender, EventArgs e)
        {
            pboxMenu[1].Checked = !pboxMenu[1].Checked;
            if (!pboxMenu[1].Checked) //Show coordinates option unchecked
                coordinates.Active = false;
        }
        private void StdScaleSet(object sender, EventArgs e)
        {
            double p = Get_default_scale();
            if (step != p)
            {
                int aa_fact = !aaMenu[AA.None].Checked ? 2 : 1;
                step = p;
                //Place picture in the center of control
                re = -2 - (p * pictureBox1.Width * aa_fact - 2.5) / 2; //[-2; 0.5]
                im = 1.15 + (p * pictureBox1.Height * aa_fact - 2.3) / 2; //[-1.15; 1.15]
                Recalculate(pictureBox1.Width, pictureBox1.Height);
            }
        }
        private void DragMouseMode(object sender, EventArgs e)
        {
            pboxMenu[3].Checked = !pboxMenu[3].Checked;
        }
        private void AntiAliasing0x(object sender, EventArgs e)
        {
            if (!aaMenu[AA.None].Checked)
            {
                aaMenu[AA.SS4x].Checked = aaMenu[AA.HR2x].Checked = false;
                aaMenu[AA.None].Checked = true;
                step = Trunc_step(step * 2);
                Recalculate(image.Width, image.Height);
            }
        }
        private void AntiAliasingSS4x(object sender, EventArgs e)
        {
            if (!aaMenu[AA.SS4x].Checked)
            {
                if (aaMenu[AA.None].Checked)
                    step = Trunc_step(step / 2);
                aaMenu[AA.None].Checked = aaMenu[AA.HR2x].Checked = false;
                aaMenu[AA.SS4x].Checked = true;
                Recalculate(image.Width, image.Height);
            }
        }
        private void AntiAliasingHR2x(object sender, EventArgs e)
        {
            if (!aaMenu[AA.HR2x].Checked)
            {
                if (aaMenu[AA.None].Checked)
                    step = Trunc_step(step / 2);
                aaMenu[AA.None].Checked = aaMenu[AA.SS4x].Checked = false;
                aaMenu[AA.HR2x].Checked = true;
                Recalculate(image.Width, image.Height);
            }
        }
        private void RenderWeighted(object sender, EventArgs e)
        {
            if (!renderMenu[0].Checked)
            {
                renderMenu[1].Checked = false;
                renderMenu[0].Checked = true;
                textBox1.Enabled = false;
                Gradient[] tg = new Gradient[pgrad.Length]; //Exchange elements of two gradient arrays
                pgrad.CopyTo(tg, 0);
                egrad.CopyTo(pgrad, 0);
                tg.CopyTo(egrad, 0);
                int ti = pgrad_count;
                pgrad_count = egrad_count;
                egrad_count = ti;
                Grad_fill(filled_gradient.Width);
                Recalculate(image.Width, image.Height, true);
            }
        }
        private void RenderContinuous(object sender, EventArgs e)
        {
            if (!renderMenu[1].Checked)
            {
                renderMenu[0].Checked = false;
                renderMenu[1].Checked = true;
                textBox1.Enabled = true;
                Gradient[] tg = new Gradient[pgrad.Length]; //Exchange elements of two gradient arrays
                pgrad.CopyTo(tg, 0);
                egrad.CopyTo(pgrad, 0);
                tg.CopyTo(egrad, 0);
                int ti = pgrad_count;
                pgrad_count = egrad_count;
                egrad_count = ti;
                Grad_fill(filled_gradient.Width);
                Recalculate(image.Width, image.Height, true);
            }
        }
        #endregion Picture_context_menu

        private double Get_default_scale() //Scale: Re [-2;0.5], Im [1.15;-1.15] 
        {
            float wsc = 2.5f / pictureBox1.Width, hsc = 2.3f / pictureBox1.Height;
            int divider = (int)((wsc > hsc ? wsc : hsc) * (1 << 16));
            if (!aaMenu[AA.None].Checked)
                divider /= 2;
            return Trunc_step((double)divider / (1 << 16));
        }

        private void Grad_fill(int grad_width)
        {
            int i, j = 0, gradRM = pgrad_count - 1;
            float lborder = pgrad[0].position * grad_width, weight;
            if (lborder > 0)
                weight = (1f - pgrad[gradRM].position + pgrad[0].position) * grad_width;
            else
                weight = (pgrad[++j].position - pgrad[0].position) * grad_width;
            for (i = 0; i < grad_width; i++) //Fill one line of gradient
            {
                //j must point to the rigth-most color from current point
                if (i >= pgrad[j].position * grad_width && j <= gradRM)
                {
                    lborder = pgrad[j++].position * grad_width; //Left border
                    if (j <= gradRM)
                        weight = pgrad[j].position * grad_width - lborder; //"Size" of one pixel
                    else
                        weight = (1f + pgrad[0].position - pgrad[gradRM].position) * grad_width;
                }
                //Linearly interpolate between two adjacent colors from gradient
                if (j == 0)
                    filled_gradient.SetPixel(i, 0, Linear_interpolate(
                        pgrad[gradRM].color, pgrad[0].color, 1f + (i - lborder) / weight));
                else if (j <= gradRM)
                    filled_gradient.SetPixel(i, 0, Linear_interpolate(
                        pgrad[j - 1].color, pgrad[j].color, (i - lborder) / weight));
                else
                    filled_gradient.SetPixel(i, 0, Linear_interpolate(
                        pgrad[gradRM].color, pgrad[0].color, (i - lborder) / weight));
            }
            pictureBox2.Refresh();
        }

        private double Trunc_step(double in_step)
        {
            //Adding step to the coordinates part with the maximum absolute value
            double p = (Math.Abs(re) > Math.Abs(im) ? re : im), f = in_step + p;
            if (f - p == 0.0) //Occurs if step has been fully truncated durind sum operations
            {
                MessageBox.Show(pictureBox1, "Limit for Double has been reached!", "Scaling error");
                return Math.Pow(2, (int)Math.Log(Math.Abs(p), 2) - 52);
            }
            else //Returns only useful part of step value
                return f - p; //This will prevent unequal pixel spacing during calculation
        }

        private void UsageStatistics()
        {
            //Get the maximum exponent of Im/Re parts of coordinates
            double expMS = Math.Log(Math.Abs(Math.Abs(re) > Math.Abs(im) ? re : im), 2);
            if (expMS >= 0)
                expMS++;
            //Difference between coordinates exponent and step exponent
            int ediff = (int)expMS - (int)Math.Log(step, 2);
            //Since "Double" type contains 53 bits for mantissa, this is the maximum difference
            label1.Text = "Double usage: " + ((ediff * 100) / 53f).ToString("F1") + "%";
            if (Vector.IsHardwareAccelerated) //If particular CPU supports vector instructions
                label1.Text += string.Format(" | {0}bit SIMD", Vector<double>.Count * 64);
        }

        private void Recalculate(int width, int height, bool onlyColor = false, 
            bool update = false, bool noRefresh = false)
        {
            if (pixelIterations.ForeColor != SystemColors.WindowText || !pictureBox1.Enabled)
                return;
            pictureBox1.Enabled = false; //Prevents refreshing and disables any events
            fileMenu.Text = "Cancel";
            if (aaMenu[AA.SS4x].Checked) //Actual size: (2*W)x(2*H)
            {
                width <<= 1;
                height <<= 1;
            }
            else if (aaMenu[AA.HR2x].Checked) //Actual size: (W+1)x(2*H+1)
            {
                width++;
                height = (height << 1) + 1;
                //[0,0];[2,0];[4,0]
                //[1,1];[3,1];[3,1]
                //[0,2];[2,2];[4,2]
            }
            int iterations = Convert.ToInt32(pixelIterations.Text);
            long total_iterations = 0;
            if (update) //Partial recalculation requested: obtain -(sum of iterations)
            {
                for (int xPixel = 0; xPixel < width; xPixel++)
                    for (int yPixel = 0; yPixel < height; yPixel++)
                    { 
                        //If maximum iterations count has been raised
                        if (onlyColor && (int)pixel_iterations[xPixel, yPixel] == prev_iterations - 1)
                            pixel_iterations[xPixel, yPixel] = 0; //Recalculate all black pixels
                        //If picture has been moved, all new pixels had already set to zero
                        total_iterations -= (int)pixel_iterations[xPixel, yPixel];
                    }
            }
            prev_iterations = iterations;
            if (!onlyColor || update)
            {
                wtime.Restart(); //Start time measurement
                progressBar1.Value = 0;
                progressBar1.Maximum = width; //Equals to maximum number of columns in the array
                timer1.Enabled = true; //This timer will throw event every 1/3s to refresh the progress bar
                GenerateMandelbrot(width, height, total_iterations, onlyColor, update, noRefresh);
                //This function calls asynchronous task, so control will be returned here very quickly
            }
            else //No recalculation required
                Render(width, height, total_iterations, onlyColor, noRefresh);
        }

        private void Render(int width, int height, long total_iterations, 
            bool onlyColor, bool noRefresh, bool noOutput = false)
        {
            wtime.Stop();
            timer1.Enabled = false;
            progressBar1.Value = 0;
            bool weighRend = renderMenu[0].Checked;
            int i, j, iterations = prev_iterations;
            int histBottom = iterations;
            float[] histogram = null;
            if (weighRend)
            {
                foreach (int q in pixel_iterations) //Searching for minimum iterations count
                    if (histBottom > q) //This will remove leading zeros in histogram
                        histBottom = q;
                i = (int)Math.Log(iterations - histBottom, 2) - 19;
                if (i > 0) //In case of >1M iterations histogram points will be packed
                    j = (1 << 19) * i + ((iterations - histBottom) >> i);
                else
                    j = iterations - histBottom;
                histogram = new float[j + 2];
                Array.Clear(histogram, 0, histogram.Length); //Explicitly set all elements to zero
            }
            long testre = BitConverter.DoubleToInt64Bits(re);
            long testim = BitConverter.DoubleToInt64Bits(im);
            long teststep = BitConverter.DoubleToInt64Bits(step);
            
            for (int xPixel = 0; xPixel < width; xPixel++)
            {
                int l;
                for (int yPixel = 0; yPixel < height; yPixel++)
                {
                    //Adding 1 to ensure that first element of histogram will be zero
                    j = (int)pixel_iterations[xPixel, yPixel] + 1; 
                     //Actual iterations will be x+3 because of double log in the calculation
                    total_iterations += j + 2;
                    if (weighRend && j != iterations)
                    {
                        l = j - histBottom;
                        if (l > (1 << 20)) //Pack iteration values >1M
                        {
                            j = (int)Math.Log(l, 2) - 19;
                            l = (1 << 19) * j + (l >> j);
                        }
                        histogram[l]++; //Increment pixel count for each iteration number
                    }
                }
            }
            if (weighRend) //Weighted rendering
            {
                //Each value will be the sum of all previous
                for (i = 1; i < histogram.Length; i++) 
                    histogram[i] += histogram[i - 1];
                float coloredPixels = histogram[histogram.Length - 1];
                //Make normalized histogram: each value will be in the range [0;1]
                for (i = 0; i < histogram.Length - 1; i++)
                    histogram[i] /= coloredPixels;
            }
            if (!noOutput) //When restoring original picture after file save
            {
                if (!onlyColor) //Show calculating time and performance in Mi per second
                {
                    label7.Text = "Mandel: " + (wtime.ElapsedMilliseconds / 1000f).ToString("F2") + " s | " +
                        ((total_iterations / wtime.ElapsedMilliseconds) / 1000f).ToString("F2") + " Mips\r\n";
                }
                else //Show overall count of iterations for current picture
                    label7.Text = "Iterations sum: " + ((double)total_iterations).ToString("G3") + "\r\n";
                UsageStatistics();
            }
            wtime.Restart();
            FillBitmap(width, height, histogram, histBottom, noRefresh);
            wtime.Stop();
            pictureBox1.Enabled = true;
            if (!noOutput) //Show rendering time and performance in Mpixel per second
            {
                label7.Text += "Render: " + (wtime.ElapsedMilliseconds / 1000f).ToString("F2") + " s | " +
                    ((image.Width * image.Height) / (wtime.ElapsedMilliseconds * 1000f)).ToString("F2") + " Mpixels";
                if (pictureBox1.ClientRectangle.Contains(pictureBox1.PointToClient(MousePosition)))
                    pictureBox1.Focus();
            }
            fileMenu.Text = "File menu";
        }

        private void GenerateMandelbrot(int width, int height, long totIters,
             bool onlyColor, bool updOnly, bool noRefresh)
        {
            // info on mandelbrot and fractals
            // https://classes.yale.edu/fractals/MandelSet/welcome.html
            if (pixel_iterations.GetLength(0) != width || height != pixel_iterations.GetLength(1))
                pixel_iterations = new float[width, height];
            _progress = 0;
            CancellationTokenSource cts = new CancellationTokenSource();
            CancellationToken token = cts.Token;
            Action callUI = () => 
            {
                _cancelWork = null;
                Render(width, height, totIters, onlyColor & !updOnly, noRefresh);
            };
            _cancelWork = () =>
            {
                cts.Cancel();
            };
            Task work = Task.Factory.StartNew(() =>
            {
                bool update = updOnly;
                int iterations = prev_iterations - 1;
                //Parallel calculation
                if (label1.ForeColor != Color.DarkGreen)
                {
                    Parallel.For(0, width,
                    (xPixel, loopState) =>
                    {
                        if (token.IsCancellationRequested)
                        {
                            loopState.Stop();
                            return;
                        }
                        bool hraa = aaMenu[AA.HR2x].Checked;
                        double reC = step * xPixel + re, imC;
                        for (int yPixel = 0; yPixel < height; yPixel++)
                        {
                            if (update && pixel_iterations[xPixel, yPixel] != 0)
                                continue;
                            imC = im - step * yPixel;
                            if (hraa) //Re component should look like sawtooth: 0, 1, 0, 1...
                                reC = re + step * ((xPixel << 1) + (yPixel & 1));
                            double reZ = 0, imZ = 0, re2 = 0, im2 = 0;
                            int iterIdx;
                            for (iterIdx = 0; iterIdx < iterations; iterIdx++)
                            {
                                double tZ = reZ * imZ;
                                tZ += tZ;
                                imZ = tZ + imC;
                                reZ = re2 - im2;
                                reZ += reC;
                                re2 = reZ * reZ;
                                im2 = imZ * imZ;
                                if (re2 + im2 > (1 << 16))
                                {
                                    pixel_iterations[xPixel, yPixel] = (float)(iterIdx + 1 -
                                        Math.Log(Math.Log(re2 + im2, 4), 2));
                                    break;
                                }
                            }
                            if (iterIdx == iterations)
                                pixel_iterations[xPixel, yPixel] = iterations;
                        }
                        Interlocked.Increment(ref _progress);
                    });
                }
                else
                {
                    int increment = Vector<double>.Count;
                    Vector<double> vMaxIters = new Vector<double>(-iterations);
                    Vector<double> vImMax = new Vector<double>(im);
                    Parallel.For(0, width, (xPixel, loopState) =>
                    {
                        if (token.IsCancellationRequested)
                        {
                            loopState.Stop();
                            return;
                        }
                        Vector<double> vReZ, vRe2, vImZ, vIm2;
                        Vector<double> vBailout = new Vector<double>(1 << 16);
                        Vector<double> vIters = vMaxIters;
                        vIm2 = vImZ = vRe2 = vReZ = Vector<double>.Zero;
                        bool hraa = aaMenu[AA.HR2x].Checked;
                        int yPixel = 0, i, j;
                        double[] t = new double[increment];
                        for (i = 0; i < increment; i++)
                        {
                            if (update) //Search for zero
                                while (yPixel < height && pixel_iterations[xPixel, yPixel] != 0)
                                    yPixel++;
                            t[i] = yPixel++;
                        }
                        Vector<double> vPixel = new Vector<double>(t);
                        Vector<double> vImC = vImMax - vPixel * step;
                        Vector<double> vReC;
                        if (!hraa)
                            vReC = new Vector<double>(re + step * xPixel);
                        else
                        {
                            for (i = 0; i < increment; i++)
                                t[i] = re + step * (((int)t[i] & 1) + (xPixel << 1));
                            vReC = new Vector<double>(t);
                        }
                        do
                        {
                            Vector<double> vTempZ = vReZ * vImZ;
                            vTempZ += vTempZ;
                            vImZ = vTempZ + vImC;
                            vReZ = vRe2 - vIm2;
                            vReZ += vReC;
                            vRe2 = vReZ * vReZ;
                            vIm2 = vImZ * vImZ;
                            vTempZ = vRe2 + vIm2;
                            vIters = vIters + Vector<double>.One;
                            vTempZ = Vector.BitwiseAnd(vTempZ - vBailout, vIters);
                            if (Vector.GreaterThanOrEqualAny(vTempZ, Vector<double>.Zero))
                            {
                                Vector<long> vCond = Vector.GreaterThanOrEqual
                                    (vTempZ, Vector<double>.Zero);
                                vPixel.CopyTo(t);
                                bool completed = true;
                                for (i = 0; i < increment; i++)
                                {
                                    j = (int)t[i];
                                    if (j >= height) continue;
                                    if (vCond[i] != 0)
                                    {
                                        if (vIters[i] >= 0) //Reached max. iterations
                                            pixel_iterations[xPixel, j] = iterations;
                                        else //Reached bailout radius
                                            pixel_iterations[xPixel, j] = (float)(vIters[i] -
                                                Math.Log(Math.Log(vRe2[i] + vIm2[i], 4), 2)) + 1 + iterations;
                                        if (update) //Search for the next zero
                                            while (yPixel < height && pixel_iterations[xPixel, yPixel] != 0)
                                                yPixel++;
                                        t[i] = yPixel++;
                                    }
                                    completed &= (j >= height); //True only when all pixels calculated
                                }
                                if (completed) break;
                                vPixel = new Vector<double>(t);
                                vIters = Vector.ConditionalSelect(vCond, vMaxIters, vIters);
                                vReZ = Vector.ConditionalSelect(vCond, Vector<double>.Zero, vReZ);
                                vRe2 = Vector.ConditionalSelect(vCond, Vector<double>.Zero, vRe2);
                                vImZ = Vector.ConditionalSelect(vCond, Vector<double>.Zero, vImZ);
                                vIm2 = Vector.ConditionalSelect(vCond, Vector<double>.Zero, vIm2);
                                vImC = vImMax - vPixel * step;
                                if (hraa)
                                {
                                    for (i = 0; i < increment; i++)
                                        t[i] = re + step * (((int)t[i] & 1) + (xPixel << 1));
                                    vReC = new Vector<double>(t);
                                }
                            }
                        } while (true);
                        Interlocked.Increment(ref _progress);
                    });
                }
                token.ThrowIfCancellationRequested();
            }, token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            work.ContinueWith(_ =>
            {
                try
                {
                    work.Wait(); //Rethrow any error
                }
                catch (Exception ex)
                {
                    while (ex is AggregateException && ex.InnerException != null)
                        ex = ex.InnerException;
                    MessageBox.Show(ex.Message, ex.Source);
                }
                callUI();
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        private void timer1_Tick(object sender, EventArgs e)
        {
            //All threads of Parallel.For incrementing this global variable
            progressBar1.Value = _progress; //Equals to currently calculated columns
        }

        private void FillBitmap(int width, int height, float[] histogram, int minIters,
            bool noRefresh)
        {
            if (aaMenu[AA.SS4x].Checked)
            {
                width >>= 1;
                height >>= 1;
            }
            else if (aaMenu[AA.HR2x].Checked)
            {
                width--;
                height >>= 1;
            }
            int gwidth = filled_gradient.Width, i, j = 0, gradRM = pgrad_count - 1;
            float lborder = pgrad[0].position * gwidth, pg = 1;
            if (lborder > 0)
                pg = 1f / ((1f - pgrad[gradRM].position + pgrad[0].position) * gwidth);
            else
                pg = 1f / ((pgrad[++j].position - pgrad[0].position) * gwidth);
            PairGradient[] palette = new PairGradient[gwidth];
            for (i = 0; i < gwidth; i++) //Create linearized gradient palette
            {
                if (pgrad[j].position * gwidth <= i && j <= gradRM)
                {
                    lborder = pgrad[j++].position * gwidth;
                    //Weight of one step between two colors
                    if (j <= gradRM)
                        pg = 1f / (pgrad[j].position * gwidth - lborder);
                    else
                        pg = 1f / ((1f - pgrad[gradRM].position + pgrad[0].position) * gwidth);
                }
                int k = (j == 0 || j > gradRM ? gradRM : j - 1);
                palette[i].left = new Vector3
                    (pgrad[k].color.R, pgrad[k].color.G, pgrad[k].color.B);
                k = (j == 0 || j > gradRM ? 0 : j);
                palette[i].diff = new Vector3
                    (pgrad[k].color.R, pgrad[k].color.G, pgrad[k].color.B);
                palette[i].diff -= palette[i].left;
                palette[i].leftBorder = pg * (i - lborder) + (j == 0 ? 1f : 0f);
                palette[i].weight = pg;
            }
            Cursor.Current = Cursors.WaitCursor;
            bool isweighted = renderMenu[0].Checked;
            byte aamode = (byte)(aaMenu[AA.None].Checked ? 
                AA.None : (aaMenu[AA.HR2x].Checked ? AA.HR2x : AA.SS4x));
            int maxIters = prev_iterations - (isweighted ? minIters : 1);
            int rrate = (textBox1.ForeColor == SystemColors.WindowText ?
                Convert.ToInt32(textBox1.Text) : maxIters);
            int[] pixelData = new int[width * height];
            Parallel.For(0, width, (xPixel) =>
            {
                float s;
                Vector3 vcolor = new Vector3(0);
                int k, p = aamode == AA.HR2x ? -2 : 0, yPixel = 0;
                byte[] icolor = new byte[4] { 0, 0, 0, 0xff };
                while (yPixel < height)
                {
                    if (aamode == AA.None)
                    {
                        s = pixel_iterations[xPixel, yPixel]; //Iterations for this pixel
                        vcolor = new Vector3(0);
                    }
                    else if (aamode == AA.SS4x) //4xSSAA: 4 values calculated for each pixel
                        s = pixel_iterations[(xPixel << 1) + (p >> 1), (yPixel << 1) + (1 & p)];
                    else //2xHRAA: 5 values calculated for each pixel (K-type)
                        s = pixel_iterations[xPixel + (1 & p), (yPixel << 1) + 1 + Math.Sign(p)];
                    if (isweighted) //In this case s is an index of histogram
                        s -= minIters;
                    k = (int)s;
                    if (k < maxIters - 1) //Index position in palette
                    {
                        if (isweighted)
                        {
                            if (k > (1 << 20))
                            {
                                k = (int)Math.Log(k, 2) - 19;
                                s = (1 << 19) * k + s / (1 << k); //Unpacking iterations
                                k = (int)s;
                            }
                            s = (s - k) * (histogram[k + 1] - histogram[k]) * gwidth + histogram[k] * gwidth;
                        }
                        else //Logarithmic coloring method
                            s = (float)(Math.Log(s / rrate + 1, 2) % 1) * gwidth;
                        k = (int)s; //s range: [0; gwidth)
                    }
                    else
                        k = gwidth;
                    if (k < gwidth)
                    {
                        //Exact position between two colors in palette
                        s = (s - k) * palette[k].weight + palette[k].leftBorder;
                        if (s > 1) //Can occur because of float rounding errors
                            s = 1;
                        if (aamode == AA.None)
                            vcolor = palette[k].left + palette[k].diff * s;
                        else if (aamode == AA.SS4x) //With 4xSSAA each value has 1/4 of total weight
                            vcolor += (palette[k].left + palette[k].diff * s) * .25f;
                        else //With 2xHRAA center value has 1/2 of total weight, 4 others has 1/8
                            vcolor += (palette[k].left + palette[k].diff * s) * (p == 0 ? .5f : .125f);
                    }
                    if (aamode == AA.None || aamode == AA.SS4x && 3 == p || p == 2 && AA.HR2x == aamode)
                    {
                        icolor[2] = (byte)vcolor.X;
                        icolor[1] = (byte)vcolor.Y;
                        icolor[0] = (byte)vcolor.Z;
                        //Write color in Argb format to pixel array
                        pixelData[xPixel + yPixel++ * width] = BitConverter.ToInt32(icolor, 0);
                    }
                    if (aamode != AA.None)
                    {
                        p++;
                        if (aamode == AA.SS4x && p > 3)
                        {
                            p = 0;
                            vcolor = new Vector3(0);
                        }
                        else if (aamode == AA.HR2x && p > 2)
                        {
                            p = -2;
                            vcolor = new Vector3(0);
                        }
                    }
                }
            });
            if (width != image.Width || image.Height != height)
            {
                Image img = pictureBox1.Image;
                if (image != null)
                    image.Dispose();
                image = new Bitmap(width, height);
                if (img != null)
                    img.Dispose();
            }
            BitmapData imageData = image.LockBits(new Rectangle(new Point(0, 0), image.Size),
                ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
            //Copy entire pixel array to bitmap structure
            Marshal.Copy(pixelData, 0, imageData.Scan0, pixelData.Length);
            image.UnlockBits(imageData);
            Cursor.Current = Cursors.Default;
            lr.Y = lr.X = 0;
            if (!noRefresh)
                pictureBox1.Refresh();
            else //"Picture save" option called, perform saving files
            {
                FileSave(saveFileDialog1.FileName, psDiag.saveFormat, psDiag.jpgQual, true);
                if (psDiag.rawRequired)
                    BinaryFileSave(saveFileDialog1.FileName);
                re = oldRe; //Restore previous image coordinates
                im = oldIm;
                step = oldStep;
                pixel_iterations = storePrev; //Reference to the previous picture data
                storePrev = null; //Delete reference copy so GC will be able to delete main array
                Render(pixel_iterations.GetLength(0), pixel_iterations.GetLength(1), 
                    0, true, false, true); //Here will be recursion
                //So we need to call function with "no text output" option
            }
        }

        private Color Linear_interpolate(Color A, Color B, float fraction)
        {
            return Color.FromArgb(
                (byte)(A.R + (B.R - A.R) * fraction),
                (byte)(A.G + (B.G - A.G) * fraction),
                (byte)(A.B + (B.B - A.B) * fraction));
        }

        private void fileMenu_Click(object sender, EventArgs e)
        {
            if (fileMenu.Text != "Cancel")
                fileopts.Show(fileMenu, new Point(0, 0));
            else _cancelWork?.Invoke(); //Call delegate that will request cancellation
        }
        #region TextProcessing
        private void pixelIterations_TextChanged(object sender, EventArgs e)
        {
            try
            {
                if (Convert.ToInt32(pixelIterations.Text) < 16)
                    throw new FormatException();
                pixelIterations.ForeColor = SystemColors.WindowText;
            }
            catch (FormatException)
            { pixelIterations.ForeColor = Color.Firebrick; }
        }
        private void textBox1_TextChanged(object sender, EventArgs e)
        {
            try
            {
                if (Convert.ToInt32(textBox1.Text) < 16)
                    throw new FormatException();
                textBox1.ForeColor = SystemColors.WindowText;
            }
            catch (FormatException)
            { textBox1.ForeColor = Color.Firebrick; }
        }
        private void label1_Click(object sender, EventArgs e)
        {
            if (label1.ForeColor != Color.DarkGreen && Vector.IsHardwareAccelerated)
                label1.ForeColor = Color.DarkGreen;
            else
                label1.ForeColor = SystemColors.ControlText;
        }
        #endregion TextProcessing

        private void pictureBox1_Paint(object sender, PaintEventArgs e)
        {
            e.Graphics.DrawImageUnscaled(image, lr);
            //Left button pressed down over picture box
            if (mouse_down.X != -1 && !pboxMenu[3].Checked)
            {
                e.Graphics.DrawRectangle(Pens.LawnGreen, new Rectangle(
                    mouse_down.X < mouse_move.X ? mouse_down.X : mouse_move.X,
                    mouse_down.Y < mouse_move.Y ? mouse_down.Y : mouse_move.Y,
                    Math.Abs(mouse_down.X - mouse_move.X), 
                    Math.Abs(mouse_down.Y - mouse_move.Y)));
            }
        }
        #region Main_picture_mouse_actions
        private void pictureBox1_MouseHover(object sender, EventArgs e)
        {
            pictureBox1.Focus();
        }
        private void pictureBox1_MouseWheel(object sender, MouseEventArgs e)
        {
            if (pixelIterations.ForeColor == SystemColors.WindowText)
            {
                double p = step, j = Get_default_scale();
                if (e.Delta > 0)
                    p /= 2; //Rotated forward
                else
                    p *= 2; //Rotated backward
                if (p > j)
                    p = j;
                else
                    p = Trunc_step(p);
                if (p != step)
                {
                    int aa_fact = !aaMenu[AA.None].Checked ? 2 : 1;
                    re += step * (e.X * aa_fact) - p * (aa_fact * image.Width >> 1);
                    im -= step * (e.Y * aa_fact) - p * (aa_fact * image.Height >> 1);
                    step = p;
                    Recalculate(pictureBox1.Width, pictureBox1.Height);
                }
            }
        }

        private void pictureBox1_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && mouse_down.Y != -1)
                    mouse_down = mouse_move = e.Location;
            if (pboxMenu[3].Checked)
                Cursor.Current = Cursors.Hand;
        }

        private void pictureBox1_MouseUp(object sender, MouseEventArgs e)
        {
            int aa_fact = !aaMenu[AA.None].Checked ? 2 : 1;
            if (e.Button == MouseButtons.Left && mouse_down.X != -1)
            {
                if (!pboxMenu[3].Checked) //Scale
                {
                    mouse_move.X = Math.Abs(e.X - mouse_down.X);
                    mouse_move.Y = Math.Abs(e.Y - mouse_down.Y);
                    int scalex = mouse_move.X * (1 << 4) / pictureBox1.Width;
                    int scaley = mouse_move.Y * (1 << 4) / pictureBox1.Height;
                    double scale = (double)(scalex > scaley ? scalex : scaley) / (1 << 4);
                    if (scale < 1 && 0 < scale)
                    {
                        mouse_move.X = (e.X < mouse_down.X ? e.X : mouse_down.X) + mouse_move.X / 2;
                        mouse_move.Y = (e.Y < mouse_down.Y ? e.Y : mouse_down.Y) + mouse_move.Y / 2;
                        scale = Trunc_step(step * scale);
                        re += step * mouse_move.X * aa_fact - scale * (aa_fact * pictureBox1.Width >> 1);
                        im -= step * mouse_move.Y * aa_fact - scale * (aa_fact * pictureBox1.Height >> 1);
                        step = scale;
                        mouse_down.X = -1;
                        Recalculate(pictureBox1.Width, pictureBox1.Height);
                    }
                    else
                    {
                        mouse_down.X = -1;
                        pictureBox1.Refresh();
                    }
                }
                else //Drag picture
                {
                    Cursor.Current = Cursors.Default;
                    int deX = (e.X - mouse_down.X) * (aaMenu[AA.SS4x].Checked ? 2 : 1);
                    int deY = (e.Y - mouse_down.Y) * aa_fact;
                    re -= step * ((e.X - mouse_down.X) * aa_fact);
                    im += step * deY;
                    mouse_down.X = -1;
                    int width = pixel_iterations.GetLength(0), height = pixel_iterations.GetLength(1);
                    if ((width - Math.Abs(deX)) * (height - Math.Abs(deY)) > (width * height) >> 4)
                    { //Area of the fragment more than 1/16 of area of the picture
                        int i, j, sp = deY < 0 ? -deY : 0, cp = deY < 0 ? 0 : deY;
                        int count = height - Math.Abs(deY);
                        if (deX > 0) //Picture has been moved to right
                        {
                            for (i = width - 1, j = width - 1 - deX; j >= 0; j--, i--)
                            {
                                Array.Copy(pixel_iterations, j * height + sp,
                                    pixel_iterations, i * height + cp, count);
                                if (cp > 0)
                                    Array.Clear(pixel_iterations, i * height, cp);
                                else
                                    Array.Clear(pixel_iterations, i * height + count, sp);
                            }
                            while (i >= 0)
                                Array.Clear(pixel_iterations, i-- * height, height);
                        }
                        else
                        {
                            for (i = 0, j = -deX; j < width; j++, i++)
                            {
                                Array.Copy(pixel_iterations, j * height + sp,
                                    pixel_iterations, i * height + cp, count);
                                if (cp > 0)
                                    Array.Clear(pixel_iterations, i * height, cp);
                                else
                                    Array.Clear(pixel_iterations, i * height + count, sp);
                            }
                            while (i < width)
                                Array.Clear(pixel_iterations, i++ * height, height);
                        }
                        Recalculate(pictureBox1.Width, pictureBox1.Height, false, true);
                    }
                    else
                        Recalculate(pictureBox1.Width, pictureBox1.Height);
                }
            }
            else if (mouse_down.Y == -1)
            {
                if (e.X < pictureBox1.Width / 25)
                {
                    re -= step * ((aa_fact * pictureBox1.Width << 2) / 5);
                    if (re < -2)
                        re = -2;
                }
                else if (e.X > (pictureBox1.Width * 24) / 25)
                {
                    re += step * ((aa_fact * pictureBox1.Width << 2) / 5);
                    if (re + (aa_fact * pictureBox1.Width) * step > 0.5)
                        re = 0.5 - (aa_fact * pictureBox1.Width) * step;
                }
                else if (e.Y < pictureBox1.Height / 25)
                {
                    im += step * ((aa_fact * pictureBox1.Height << 2) / 5);
                    if (im > 1.15)
                        im = 1.15;
                }
                else if (e.Y > (pictureBox1.Height * 24) / 25)
                {
                    im -= step * ((aa_fact * pictureBox1.Height << 2) / 5);
                    if (im - (aa_fact * pictureBox1.Height) * step < -1.15)
                        im = (aa_fact * pictureBox1.Height) * step - 1.15;
                }
                Recalculate(pictureBox1.Width, pictureBox1.Height);
            }
            pictureBox1.Focus();
        }

        private void pictureBox1_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && mouse_down.X != -1)
            {
                mouse_move = e.Location;
                if (pboxMenu[3].Checked) //If picture moving enabled
                {
                    lr.X = e.X - mouse_down.X;
                    lr.Y = e.Y - mouse_down.Y;
                }
                pictureBox1.Refresh();
            }
            else
            {
                if (e.X < pictureBox1.Width / 25)
                    Cursor.Current = Cursors.PanWest;
                else if (e.X > (pictureBox1.Width * 24) / 25)
                    Cursor.Current = Cursors.PanEast;
                else if (e.Y < pictureBox1.Height / 25)
                    Cursor.Current = Cursors.PanNorth;
                else if (e.Y > (pictureBox1.Height * 24) / 25)
                    Cursor.Current = Cursors.PanSouth;
                else
                    Cursor.Current = Cursors.Default;

                if (Cursor.Current != Cursors.Default)
                    mouse_down.Y = -1;
                else
                    mouse_down.Y = 0;
                if (pboxMenu[1].Checked)
                {
                    if (mouse_move.X != e.X || e.Y != mouse_move.Y)
                    {
                        int aa_fact = !aaMenu[AA.None].Checked ? 2 : 1;
                        double imT = im - e.Y * step * aa_fact;
                        coordinates.SetToolTip(pictureBox1,
                            (re + e.X * step * aa_fact).ToString() + 
                            (imT >= 0 ? "+" : "") + imT.ToString() + "i");
                        mouse_move = e.Location;
                    }
                }
            }
        }
        #endregion Main_picture_mouse_actions

        private void pictureBox2_Paint(object sender, PaintEventArgs e)
        {
            int y, z, lborder = pictureBox2.Width / 42;
            for (y = 0; y < pictureBox2.Height >> 1; y++)
                e.Graphics.DrawImageUnscaled(filled_gradient, lborder, y);
            if (renderMenu[1].Checked)
            {
                float x = pictureBox2.Height / 4f;
                e.Graphics.DrawLine(arrowPen, new PointF(shiftGrad.X, x), new PointF(shiftGrad.Y, x));
            }
            for (z = 0; z < pgrad_count; z++)
            {
                e.Graphics.DrawImageUnscaled(g_arrow,
                    lborder + (int)(pgrad[z].position * filled_gradient.Width - 
                    g_arrow.Width / 2), y);
            }
        }
        #region Gradient_context_menu
        private void GradColorSet(object sender, EventArgs e)
        {
            if (selected_grad > -2 && pgrad_count > selected_grad &&
                choosecolor.ShowDialog() == DialogResult.OK)
            {
                if (nextposition == -1) //Change existing element
                    pgrad[selected_grad].color = choosecolor.Color;
                else if (pgrad_count < pgrad.Length) //Add new element
                {
                    selected_grad++;
                    for (int i = pgrad_count; i > selected_grad && 0 < i; i--) //Shift elements left
                        pgrad[i] = pgrad[i - 1];
                    //Write new element
                    pgrad[selected_grad] = new Gradient(choosecolor.Color, nextposition);
                    pgrad_count++;
                }
                Grad_fill(filled_gradient.Width);
                PboxUpdate(sender, e);
            }
        }

        private void GradDeletePt(object sender, EventArgs e)
        {
            if (selected_grad > -1 && pgrad_count > selected_grad)
            {
                for (int i = selected_grad; i < pgrad_count; i++) //Shift elements left
                    pgrad[i] = pgrad[i + 1];
                pgrad_count--; //Now we have less gradient points
                Grad_fill(filled_gradient.Width);
                PboxUpdate(sender, e);
            }
        }
        #endregion Gradient_context_menu

        #region Gradient_picture_mouse_actions
        private void pictureBox2_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                if (e.Y > pictureBox2.Height >> 1) //Lower half clicked
                {
                    int i, j;
                    selected_grad = -1;
                    for (i = 0; i < pgrad_count; i++)
                    {
                        j = (int)(pgrad[i].position * filled_gradient.Width - g_arrow.Width / 2)
                            + pictureBox2.Width / 42;
                        if (j <= e.X && e.X <= j + g_arrow.Width)
                        {
                            selected_grad = i; //Clicked on the arrow
                            i = pgrad_count;
                        }
                    }
                    if (renderMenu[0].Checked && 
                        (1 > selected_grad || selected_grad + 2 > pgrad_count))
                        selected_grad = -1; //Disable movement of border points in weighted coloring
                }
                else //Upper half clicked
                    shiftGrad = new Point(e.X, e.X);
            }
        }

        private void pictureBox2_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right && e.Y > pictureBox2.Height >> 1)
            {
                int i, j, l = -1;
                selected_grad = -2;
                nextposition = -1;
                for (i = -1; i < pgrad_count; i++)
                {
                    j = pictureBox2.Width / 42;
                    if (i >= 0)
                        j += (int)(pgrad[i].position * filled_gradient.Width - g_arrow.Width / 2);
                    if (e.X <= j + g_arrow.Width)
                    {
                        if (j <= e.X) //If clicked on arrow, remember index
                        {
                            selected_grad = i;
                            i = pgrad_count;
                        }
                    }
                    else
                        l = i; //Here we're getting nearest neighbour index from the left for the new arrow
                }
                if (renderMenu[0].Checked && 0 < selected_grad && selected_grad + 1 < pgrad_count ||
                    renderMenu[1].Checked && selected_grad >= 0 && 2 < pgrad_count)
                {
                    gboxMenu[0].Enabled = true; //Change color
                    gboxMenu[1].Enabled = true; //Delete
                }
                else
                {
                    gboxMenu[1].Enabled = false; //Delete opt. disabled for border points
                    if (pgrad_count >= pgrad.Length)
                        gboxMenu[0].Enabled = false; //Maximum number of points reached
                    else
                        gboxMenu[0].Enabled = true;
                    if (selected_grad < 0 && l > -2 && l < pgrad_count)
                    {
                        selected_grad = l;
                        nextposition = (e.X - pictureBox2.Width / 42) / (float)filled_gradient.Width;
                        if (nextposition < 0.02f)
                            nextposition = 0.02f;
                        else if (nextposition > 0.98f)
                            nextposition = 0.98f;
                    }
                }
                gbox.Show(pictureBox2, e.Location);
            }
        }

        private void pictureBox2_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                if (e.Y > pictureBox2.Height >> 1)
                {
                    if (selected_grad < 0)
                        return;
                    float pos = (e.X - pictureBox2.Width / 42) / (float)filled_gradient.Width;
                    //Position of selected arrow must be between two adjacent arrows
                    float leftb = selected_grad > 0 ? 
                        pgrad[selected_grad - 1].position + 0.02f : 0;
                    if (selected_grad == 0 && pgrad[pgrad_count - 1].position >= 0.98f)
                        leftb = 1f - pgrad[pgrad_count - 1].position;
                    float rightb = selected_grad < pgrad_count - 1 ? 
                        pgrad[selected_grad + 1].position - 0.02f : 1f;
                    if (selected_grad == pgrad_count - 1 && pgrad[0].position <= 0.02f)
                        rightb = 1f - pgrad[0].position;
                    if (rightb > pos && pos > leftb)
                        pgrad[selected_grad].position = pos;
                }
                else
                    shiftGrad.Y = e.X;
                pictureBox2.Refresh();
            }
        }

        private void pictureBox2_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                if (shiftGrad.X != shiftGrad.Y)
                {
                    float shifted = (shiftGrad.Y - shiftGrad.X) / (float)filled_gradient.Width;
                    //Shifted > 0 -> right direction, < 0 -> left direction
                    int i, idx = -1;
                    for (i = 0; i < pgrad_count; i++)
                    {
                        pgrad[i].position += shifted;
                        if (shifted > 0)
                        {
                            if (pgrad[i].position > 1f)
                            {
                                pgrad[i].position -= 1f;
                                if (idx == -1)
                                    idx = i; //First position out of bounds
                            }
                        }
                        else if (pgrad[i].position < 0f)
                        {
                            pgrad[i].position += 1f;
                            idx = i + 1; //First position in bounds
                        }
                    }
                    if (0 < idx && idx < pgrad_count) //Rotation of gradient points is required
                    {
                        Gradient[] t = new Gradient[pgrad.Length];
                        Array.Copy(pgrad, t, pgrad.Length); //Copy elements
                        //Move one part of elements to the beginning
                        Array.Copy(t, idx, pgrad, 0, pgrad_count - idx);
                        Array.Copy(t, 0, pgrad, pgrad_count - idx, idx);
                    }
                }
                shiftGrad = new Point(0, 0);
                if (selected_grad != -2)
                    Grad_fill(filled_gradient.Width);
                PboxUpdate(sender, new EventArgs());
            }
        }
        #endregion Gradient_picture_mouse_actions

        #region Main_form_events
        private void Form1_ResizeEnd(object sender, EventArgs e)
        {
            form_resizing = false;
            Form1_Resize(sender, e);
        }

        private void Form1_ResizeBegin(object sender, EventArgs e)
        {
            form_resizing = true;
        }

        private void Form1_Resize(object sender, EventArgs e)
        {
            if (WindowState != FormWindowState.Minimized)
            {
                if (!form_resizing && !prevStateMinimized)
                {
                    if (pictureBox1.Width != image.Width || pictureBox1.Height != image.Height)
                    {
                        int aa_fact = !aaMenu[AA.None].Checked ? 2 : 1;
                        float sWidth = image.Width / (float)pictureBox1.Width;
                        float sHeight = image.Height / (float)pictureBox1.Height;
                        if (sWidth < sHeight)
                            sWidth = sHeight;
                        oldStep = step;
                        step = Trunc_step(sWidth * step);
                        re += oldStep * (image.Width * aa_fact >> 1) - step * (pictureBox1.Width * aa_fact >> 1);
                        im += step * (pictureBox1.Height * aa_fact >> 1) - oldStep * (image.Height * aa_fact >> 1);
                        Recalculate(pictureBox1.Width, pictureBox1.Height);
                    }
                }
                prevStateMinimized = false;
            }
            else
                prevStateMinimized = true;
        }
        private void Form1_Load(object sender, EventArgs e)
        {
            //Default values for gradient
            pgrad[0] = new Gradient(Color.DarkBlue, 0f);
            pgrad[1] = new Gradient(Color.White, 0.73f);
            pgrad[2] = new Gradient(Color.FromArgb(0xFF, 0x80, 0), 0.86f);
            pgrad[3] = new Gradient(Color.FromArgb(0, 0x80, 0xC0), 0.94f);
            pgrad[4] = new Gradient(Color.White, 0.97f);
            pgrad[5] = new Gradient(Color.FromArgb(0xFF, 0x80, 0), 1f);
            //Default values for exponential rendering
            egrad[0] = new Gradient(Color.DarkBlue, 0f);
            egrad[1] = new Gradient(Color.White, 0.25f);
            egrad[2] = new Gradient(Color.DarkOrange, 0.75f);
            egrad[3] = new Gradient(Color.DarkBlue, 1f);
            //Configuring context menu for main picture
            pboxMenu[0].Click += new EventHandler(StdScaleSet);
            pboxMenu[1].Click += new EventHandler(SwitchCoordinates);
            pboxMenu[2].Click += new EventHandler(OpenCoordinatesDiag);
            pboxMenu[3].Click += new EventHandler(DragMouseMode);
            pbox.MenuItems.AddRange(pboxMenu);
            renderMenu[0].Checked = true;
            renderMenu[0].Click += new EventHandler(RenderWeighted);
            renderMenu[1].Click += new EventHandler(RenderContinuous);
            pbox.MenuItems.Add("Rendering mode", renderMenu);
            aaMenu[AA.None].Checked = true;
            aaMenu[AA.None].Click += new EventHandler(AntiAliasing0x);
            aaMenu[AA.HR2x].Click += new EventHandler(AntiAliasingHR2x);
            aaMenu[AA.SS4x].Click += new EventHandler(AntiAliasingSS4x);
            pbox.MenuItems.Add("Anti-aliasing", aaMenu);
            pbox.MenuItems.Add("Update", new EventHandler(PboxUpdate));
            pictureBox1.ContextMenu = pbox;
            coordinates.AutomaticDelay = 666;
            coordinates.ReshowDelay = 666;
            coordinates.InitialDelay = 1337;
            //File menu configuration
            fileoptsMenu[0].Click += new EventHandler(DataFileLoad);
            fileoptsMenu[1].Click += new EventHandler(DataFileSave);
            fileoptsMenu[2].Click += new EventHandler(PictureSave);
            fileopts.MenuItems.AddRange(fileoptsMenu);
            //Gradient strip for gradient control
            filled_gradient = new Bitmap(pictureBox2.Width * 20 / 21, 1);
            //Arrow with caps for gradiend shift
            arrowPen.Width = pictureBox2.Height / 12f;
            arrowBrush = new System.Drawing.Drawing2D.LinearGradientBrush(
                new Point(0, 0), new Point(pictureBox2.Width, 0), Color.LimeGreen, Color.Plum);
            arrowPen.Brush = arrowBrush;
            arrowPen.StartCap = System.Drawing.Drawing2D.LineCap.RoundAnchor;
            arrowPen.EndCap = System.Drawing.Drawing2D.LineCap.ArrowAnchor;
            //Create bottom arrow for color positioners
            g_arrow = new Bitmap(filled_gradient.Width / 50, pictureBox2.Height / 3);
            using (Graphics g = Graphics.FromImage(g_arrow))
            {
                g.Clear(SystemColors.Control);
                g.FillPolygon(Brushes.Black, new PointF[5] {
                    new PointF(0, g_arrow.Width), new PointF(g_arrow.Width / 2.0f, 0),
                    new PointF(g_arrow.Width, g_arrow.Width), new PointF(g_arrow.Width, g_arrow.Height),
                    new PointF(0, g_arrow.Height) });
            }
            //Configuring context menu for gradient control
            gboxMenu[0].Click += new EventHandler(GradColorSet);
            gboxMenu[1].Click += new EventHandler(GradDeletePt);
            gbox.MenuItems.AddRange(gboxMenu);
            choosecolor.SolidColorOnly = true;

            FileOpen("settings.jpg");
            if (Vector.IsHardwareAccelerated)
                label1.ForeColor = Color.DarkGreen;
        }

        private void Form1_FormClosed(object sender, FormClosedEventArgs e)
        {
            FileSave("settings.jpg", ImageFormat.Jpeg.Guid, 75);
        }
        #endregion Main_form_events
        private void DataFileLoad(object sender, EventArgs e)
        {
            if (openFileDialog1.ShowDialog() == DialogResult.OK)
                FileOpen(openFileDialog1.FileName);
        }
        private void DataFileSave(object sender, EventArgs e)
        {
            saveFileDialog1.FileName = "settings_" + DateTime.Now.ToString("ddMMM_HH-mm");
            saveFileDialog1.Filter = "JPEG (*.jpg)|*.jpg";
            if (saveFileDialog1.ShowDialog() == DialogResult.OK)
                FileSave(saveFileDialog1.FileName, ImageFormat.Jpeg.Guid, 75);
        }
        private void PictureSave(object sender, EventArgs e)
        {
            saveFileDialog1.FileName = DateTime.Now.ToString("ddMMM_HH-mm");
            saveFileDialog1.Filter = "JPEG (*.jpg)|*.jpg|TIFF (*.tiff)|*.tiff|PNG (*.png)|*.png";
            if (saveFileDialog1.ShowDialog() != DialogResult.OK)
                return;
            psDiag.sfw = image.Width;
            psDiag.sfh = image.Height;
            psDiag.saveFormat = saveFormat[saveFileDialog1.FilterIndex - 1];
            if (psDiag.ShowDialog() == DialogResult.OK)
            {
                int aa_fact = aaMenu[AA.None].Checked ? 1 : 2;
                if (psDiag.sfh == image.Height && psDiag.sfw == image.Width)
                {
                    FileSave(saveFileDialog1.FileName, psDiag.saveFormat, psDiag.jpgQual, true);
                    if (psDiag.rawRequired)
                        BinaryFileSave(saveFileDialog1.FileName);
                }
                else //Redrawing of the picture required
                {
                    oldStep = step;
                    oldRe = re;
                    oldIm = im;
                    storePrev = pixel_iterations;
                    //Coordinates and array pointer saved
                    float sWidth = image.Width / (float)psDiag.sfw;
                    float sHeight = image.Height / (float)psDiag.sfh;
                    if (psDiag.cropMode && sHeight < sWidth || sWidth < sHeight)
                        sWidth = sHeight;
                    step = Trunc_step(sWidth * step);
                    re += oldStep * (image.Width * aa_fact >> 1) - step * (psDiag.sfw * aa_fact >> 1);
                    im += step * (psDiag.sfh * aa_fact >> 1) - oldStep * (image.Height * aa_fact >> 1);
                    Recalculate(psDiag.sfw, psDiag.sfh, false, false, true);
                    //This function calls asynchronous task, so file saving will be in the other function
                }
            }
        }
        private ImageCodecInfo FindEncoder(Guid g)
        {
            foreach (ImageCodecInfo info in ImageCodecInfo.GetImageEncoders())
                if (info.FormatID.Equals(g))
                    return info;
            return null;
        }

        private void FileOpen(string filename)
        {
            Image metadata = null;
            try { metadata = Image.FromFile(filename); }
            catch (Exception e)
            {
                if (e is FileNotFoundException)
                {
                    Grad_fill(filled_gradient.Width);
                    StdScaleSet(new object(), new EventArgs());
                }
                return;
            }
            PropertyItem propItem, propGradA, propGradB;
            try 
            { 
                propItem = metadata.GetPropertyItem(0x9290);
                propGradA = metadata.GetPropertyItem(0x9291);
                propGradB = metadata.GetPropertyItem(0x9292);
            }
            catch (Exception e)
            {
                if (e is ArgumentException)
                    MessageBox.Show("This picture doesn't contain necessary metadata");
                else
                    MessageBox.Show(e.Message, e.Source);
                metadata.Dispose();
                return;
            }
            if (BitConverter.ToSingle(propItem.Value, 0) != settingsVersion)
            {
                MessageBox.Show("Old or unsupported metadata");
                metadata.Dispose();
                return;
            }
            aaMenu[AA.None].Checked = propItem.Value[4] == AA.None;
            aaMenu[AA.HR2x].Checked = propItem.Value[4] == AA.HR2x;
            aaMenu[AA.SS4x].Checked = propItem.Value[4] == AA.SS4x;
            textBox1.Enabled = renderMenu[1].Checked =
                !(renderMenu[0].Checked = BitConverter.ToBoolean(propItem.Value, 5));
            pboxMenu[1].Checked = BitConverter.ToBoolean(propItem.Value, 6);
            pboxMenu[3].Checked = BitConverter.ToBoolean(propItem.Value, 7);
            pixelIterations.Text = BitConverter.ToInt32(propItem.Value, 8).ToString();
            textBox1.Text = BitConverter.ToInt32(propItem.Value, 12).ToString();
            pgrad_count = BitConverter.ToInt32(propItem.Value, 16);
            egrad_count = BitConverter.ToInt32(propItem.Value, 20);
            int pWidth = BitConverter.ToInt32(propItem.Value, 24);
            int pHeight = BitConverter.ToInt32(propItem.Value, 28);
            re = BitConverter.ToDouble(propItem.Value, 32);
            im = BitConverter.ToDouble(propItem.Value, 40);
            step = BitConverter.ToDouble(propItem.Value, 48);
            for (int i = 0; i < pgrad_count; i++)
            {
                pgrad[i].color = Color.FromArgb(BitConverter.ToInt32(propGradA.Value, i * 8));
                pgrad[i].position = BitConverter.ToSingle(propGradA.Value, i * 8 + 4);
            }
            for (int i = 0; i < egrad_count; i++)
            {
                egrad[i].color = Color.FromArgb(BitConverter.ToInt32(propGradB.Value, i * 8));
                egrad[i].position = BitConverter.ToSingle(propGradB.Value, i * 8 + 4);
            }
            if (renderMenu[1].Checked) //Logarithmic coloring
            {
                if (pgrad[0].position == 0 && pgrad[pgrad_count - 1].position == 1f)
                    pgrad_count--; //Remove duplicating point from old metadata version
            }
            else if (egrad[0].position == 0 && egrad[egrad_count - 1].position == 1f)
                egrad_count--;
            metadata.Dispose();

            FileStream fstream = null;
            bool piFilled = false;
            int sWidth = pWidth, sHeight = pHeight;
            if (aaMenu[AA.HR2x].Checked)
            {
                sWidth++;
                sHeight = (pHeight << 1) + 1;
            }
            else if (aaMenu[AA.SS4x].Checked)
            {
                sWidth = pWidth << 1;
                sHeight = pHeight << 1;
            }
            try
            {
                fstream = new FileStream(Path.ChangeExtension(filename, "bin"), FileMode.Open, FileAccess.Read);
                if ((fstream.Length >> 2) != (sWidth * sHeight)) //Compare size in bytes
                    MessageBox.Show("Incorrect size of binary file");
                else
                {
                    pictureBox1.Enabled = false;
                    if (Screen.PrimaryScreen.Bounds.Width < pWidth ||
                        Screen.PrimaryScreen.Bounds.Height + pictureBox1.Height < Height + pHeight)
                        MessageBox.Show("Picture won't fit on the current screen");
                    else
                    {
                        Height += pHeight - pictureBox1.Height;
                        Width += pWidth - pictureBox1.Width;
                    }
                    if (pixel_iterations.GetLength(0) != sWidth ||
                        pixel_iterations.GetLength(1) != sHeight)
                        pixel_iterations = new float[sWidth, sHeight];
                    using (BinaryReader brd = new BinaryReader(fstream))
                    {
                        for (int a = 0; a < pixel_iterations.GetLength(0); a++)
                        {
                            for (int b = 0; b < pixel_iterations.GetLength(1); b++)
                                pixel_iterations[a, b] = brd.ReadSingle();
                        }
                    }
                    piFilled = true;
                }
            }
            catch (Exception e)
            {
                if (e is FileNotFoundException)
                    piFilled = false;
                else
                    MessageBox.Show(e.Message);
            }
            finally
            {
                if (fstream != null)
                    fstream.Close();
            }
            Grad_fill(filled_gradient.Width);
            if (!piFilled)
            {
                float xHeight = pHeight / (float)pictureBox1.Height, yWidth = pWidth / (float)pictureBox1.Width;
                if (xHeight > yWidth)
                    yWidth = xHeight;
                step = Trunc_step(yWidth * step);
                re -= step * (aaMenu[AA.None].Checked ? pictureBox1.Width >> 1 : pictureBox1.Width);
                im += step * (aaMenu[AA.None].Checked ? pictureBox1.Height >> 1 : pictureBox1.Height);
                pictureBox1.Enabled = true;
                Recalculate(pictureBox1.Width, pictureBox1.Height);
            }
            else
            {
                re -= step * (sWidth >> 1);
                im += step * (sHeight >> 1);
                prev_iterations = Convert.ToInt32(pixelIterations.Text);
                Render(sWidth, sHeight, 0, true, false);
            }
        }

        private void FileSave(string filename, Guid format, long jpgQual, bool fullSize = false)
        {
            ImageCodecInfo codec = FindEncoder(format);
            EncoderParameters encParams = new EncoderParameters(1);
            encParams.Param[0] = new EncoderParameter(Encoder.Quality, jpgQual);
            Bitmap result = null;
            if (!fullSize)
            {
                result = new Bitmap(640, image.Height * 640 / image.Width);
                using (Graphics g = Graphics.FromImage(result))
                {
                    g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                    g.DrawImage(image, 0, 0, result.Width, result.Height);
                }
            }
            PropertyItem propItem = (PropertyItem)FormatterServices.GetUninitializedObject(typeof(PropertyItem));
            PropertyItem propGradA = (PropertyItem)FormatterServices.GetUninitializedObject(typeof(PropertyItem));
            PropertyItem propGradB = (PropertyItem)FormatterServices.GetUninitializedObject(typeof(PropertyItem));
            propItem.Id = 0x9290;
            propGradA.Id = 0x9291;
            propGradB.Id = 0x9292;
            propItem.Type = propGradA.Type = propGradB.Type = 1;
            propItem.Len = 56;
            propItem.Value = new byte[propItem.Len];
            propGradA.Len = pgrad_count * 8;
            propGradA.Value = new byte[propGradA.Len];
            propGradB.Len = egrad_count * 8;
            propGradB.Value = new byte[propGradB.Len];
            BitConverter.GetBytes(settingsVersion).CopyTo(propItem.Value, 0);
            propItem.Value[4] = (byte)(aaMenu[AA.None].Checked ?
                AA.None : (aaMenu[AA.HR2x].Checked ? AA.HR2x : AA.SS4x));
            BitConverter.GetBytes(renderMenu[0].Checked).CopyTo(propItem.Value, 5);
            BitConverter.GetBytes(pboxMenu[1].Checked).CopyTo(propItem.Value, 6);
            BitConverter.GetBytes(pboxMenu[3].Checked).CopyTo(propItem.Value, 7);
            BitConverter.GetBytes(prev_iterations).CopyTo(propItem.Value, 8);
            BitConverter.GetBytes(textBox1.ForeColor == SystemColors.WindowText ?
                Convert.ToInt32(textBox1.Text) : prev_iterations).CopyTo(propItem.Value, 12);
            BitConverter.GetBytes(pgrad_count).CopyTo(propItem.Value, 16);
            BitConverter.GetBytes(egrad_count).CopyTo(propItem.Value, 20);
            BitConverter.GetBytes(image.Width).CopyTo(propItem.Value, 24);
            BitConverter.GetBytes(image.Height).CopyTo(propItem.Value, 28);
            int aaFact = !aaMenu[AA.None].Checked ? 2 : 1;
            BitConverter.GetBytes(re + step * (aaFact * image.Width >> 1)).CopyTo(propItem.Value, 32);
            BitConverter.GetBytes(im - step * (aaFact * image.Height >> 1)).CopyTo(propItem.Value, 40);
            BitConverter.GetBytes(step).CopyTo(propItem.Value, 48);
            for (int i = 0; i < pgrad_count; i++)
            {
                BitConverter.GetBytes(pgrad[i].color.ToArgb()).CopyTo(propGradA.Value, i * 8);
                BitConverter.GetBytes(pgrad[i].position).CopyTo(propGradA.Value, i * 8 + 4);
            }
            for (int i = 0; i < egrad_count; i++)
            {
                BitConverter.GetBytes(egrad[i].color.ToArgb()).CopyTo(propGradB.Value, i * 8);
                BitConverter.GetBytes(egrad[i].position).CopyTo(propGradB.Value, i * 8 + 4);
            }
            if (result != null)
            {
                result.SetPropertyItem(propItem);
                result.SetPropertyItem(propGradA);
                result.SetPropertyItem(propGradB);
            }
            else
            {
                image.SetPropertyItem(propItem);
                image.SetPropertyItem(propGradA);
                image.SetPropertyItem(propGradB);
            }
            try
            {
                propItem.Id = 0x10E;
                propItem.Type = 2;
                propItem.Value = System.Text.Encoding.UTF8.GetBytes(Text + "\0");
                propItem.Len = Text.Length + 1;
                if (result != null)
                {
                    result.SetPropertyItem(propItem);
                    result.Save(filename, codec, encParams);
                    result.Dispose();
                }
                else
                {
                    image.SetPropertyItem(propItem);
                    image.Save(filename, codec, encParams);
                }
            }
            catch (SystemException ex)
            {
                MessageBox.Show(ex.Message, ex.Source);
            }
        }

        private void BinaryFileSave(string filename)
        {
            FileStream fstream = null;
            try
            {
                fstream =  new FileStream(Path.ChangeExtension(filename, "bin"), 
                    FileMode.Create, FileAccess.Write);
                using (BinaryWriter bwr = new BinaryWriter(fstream))
                {
                    foreach (float a in pixel_iterations)
                        bwr.Write(a);
                    fstream.Flush();
                }
            }
            catch (Exception e)
            { MessageBox.Show(e.Message, "Binary data cannot be saved!"); }
            finally
            {
                if (fstream != null)
                    fstream.Close();
            }
        }
    }
}