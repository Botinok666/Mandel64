using System;
using System.Collections.Generic;
//using System.ComponentModel;
//using System.Linq;
using System.IO;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Diagnostics;

namespace Mandel
{
    public partial class Main : Form
    {
        Bitmap filled_gradient = new Bitmap(200, 1), g_arrow = new Bitmap(5, 20);
        Bitmap image = new Bitmap(64, 48);
        bool form_resizing = false, prevStateMinimized = false, saveLoaded = false;
        int selected_grad = -1, pgrad_count = 6, egrad_count = 4, prev_iterations = 4, extVer = 0;
        float nextposition = -1;
        float[,] pixel_iterations = new float[2, 2], storePrev = null;
        float[] gHistogram = null;
        double re = -2, im = 1.15, step = 26.0 / (1 << 16);
        double oldRe = -2, oldIm = 1.15, oldStep = 2e-6;
        Point mouse_down = new Point(-1, 0), mouse_move = new Point(-1, 0), lr = new Point(0, 0);
        Point shiftGrad = new Point(0, 0);
        RectangleF saveFrame = new RectangleF(0, 0, 0, 0);
        private readonly ContextMenu pbox = new ContextMenu();
        private readonly ContextMenu gbox = new ContextMenu();
        private readonly ContextMenu fileopts = new ContextMenu();
        MenuItem[] aaMenu = new MenuItem[] { new MenuItem("None"), new MenuItem("2xHRAA"), new MenuItem("4xSSAA") },
            renderMenu = new MenuItem[] { new MenuItem("Weighted"), new MenuItem("Logarithmic") },
            pboxMenu = new MenuItem[] { new MenuItem("Set standard scale"), new MenuItem("Show coordinates"),
                new MenuItem("Manually set coordinates"), new MenuItem("Drag picture by mouse")},
            gboxMenu = new MenuItem[] { new MenuItem("Change color"), new MenuItem("Delete") },
            fileoptsMenu = new MenuItem[] { new MenuItem("Open coordinates file"),
                new MenuItem("Save coordinates file"), new MenuItem("Save picture") };
        readonly ColorDialog choosecolor = new ColorDialog();
        readonly ToolTip coordinates = new ToolTip();
        SaveParams saveParams = new SaveParams() { tileX = 1, tileY = 1 };
        readonly Stopwatch wtime = new Stopwatch();
        const float settingsVersion = 1.05f;
        readonly string[] extensionList = new string[] { "FPU", "SSE2", "AVX" };

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
            public float leftR, leftG, leftB;
            public float leftBorder;
            public float diffR, diffG, diffB;
            public float weight;
        }
        struct AA
        {
            public const int None = 0;
            public const int SS4x = 2;
            public const int HR2x = 1;
        }
        Gradient[] pgrad = new Gradient[16], egrad = new Gradient[16];
        PairGradient[] gPalette;
        Brush arrowBrush;
        Pen arrowPen = new Pen(Color.Orchid);

        public List<string> BinList { get; set; } = new List<string>();

        [DllImport("MandelCore64.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int GetLatestSupportedExtension();
        [DllImport("MandelCore64.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern void Mandel64_SSE2(ref float Arr, double Re, double Im, 
            double Step, int Iterations, int Mode, int Height, int xPixel);
        [DllImport("MandelCore64.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern void Mandel64_AVX(ref float Arr, double Re, double Im,
            double Step, int Iterations, int Mode, int Height, int xPixel);

        public Main()
        {
            InitializeComponent();
            Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal;
        }

        #region Picture_context_menu
        private async void PboxUpdate(object sender, EventArgs e)
        {
            //Perform only rendering of black pixels if iterations count raised
            if (pixelIterations.ForeColor == SystemColors.WindowText)
            {
                int i = Convert.ToInt32(pixelIterations.Text);
                await RecalculateAsync(image.Size, i >= prev_iterations, i > prev_iterations);
            }
        }
        private async void OpenCoordinatesDiag(object sender, EventArgs e)
        {
            using (Coordinates open_diag = new Coordinates())
            {
                open_diag.re = re;
                open_diag.im = im;
                open_diag.step = step;
                if (open_diag.ShowDialog() == DialogResult.OK)
                {
                    re = open_diag.re;
                    im = open_diag.im;
                    step = open_diag.step;
                    await RecalculateAsync(image.Size);
                }
            }
        }
        private void SwitchCoordinates(object sender, EventArgs e)
        {
            pboxMenu[1].Checked = !pboxMenu[1].Checked;
            if (!pboxMenu[1].Checked) //Show coordinates option unchecked
                coordinates.Active = false;
        }
        private async void StdScaleSet(object sender, EventArgs e)
        {
            double p = Get_default_scale();
            if (step != p)
            {
                int aa_fact = !aaMenu[AA.None].Checked ? 2 : 1;
                step = p;
                //Place picture in the center of control
                re = -2 - (p * pictureBox1.Width * aa_fact - 2.5) / 2; //[-2; 0.5]
                im = 1.15 + (p * pictureBox1.Height * aa_fact - 2.3) / 2; //[-1.15; 1.15]
                await RecalculateAsync(pictureBox1.Size);
            }
        }
        private void DragMouseMode(object sender, EventArgs e)
        {
            pboxMenu[3].Checked = !pboxMenu[3].Checked;
        }
        private async void AntiAliasing0x(object sender, EventArgs e)
        {
            if (!aaMenu[AA.None].Checked)
            {
                aaMenu[AA.SS4x].Checked = aaMenu[AA.HR2x].Checked = false;
                aaMenu[AA.None].Checked = true;
                step = Trunc_step(step * 2);
                await RecalculateAsync(image.Size);
            }
        }
        private async void AntiAliasingSS4x(object sender, EventArgs e)
        {
            if (!aaMenu[AA.SS4x].Checked)
            {
                if (aaMenu[AA.None].Checked)
                    step = Trunc_step(step / 2);
                aaMenu[AA.None].Checked = aaMenu[AA.HR2x].Checked = false;
                aaMenu[AA.SS4x].Checked = true;
                await RecalculateAsync(image.Size);
            }
        }
        private async void AntiAliasingHR2x(object sender, EventArgs e)
        {
            if (!aaMenu[AA.HR2x].Checked)
            {
                if (aaMenu[AA.None].Checked)
                    step = Trunc_step(step / 2);
                aaMenu[AA.None].Checked = aaMenu[AA.SS4x].Checked = false;
                aaMenu[AA.HR2x].Checked = true;
                await RecalculateAsync(image.Size);
            }
        }
        private async void RenderWeighted(object sender, EventArgs e)
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
                await RecalculateAsync(image.Size, true);
            }
        }
        private async void RenderContinuous(object sender, EventArgs e)
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
                await RecalculateAsync(image.Size, true);
            }
        }
        #endregion Picture_context_menu
        /// <summary>
        /// Calculates coordinates and step so whole Mandel set can be dispayed
        /// </summary>
        /// <returns>New step value</returns>
        private double Get_default_scale() //Scale: Re [-2;0.5], Im [1.15;-1.15] 
        {
            float wsc = 2.5f / pictureBox1.Width, hsc = 2.3f / pictureBox1.Height;
            int divider = (int)((wsc > hsc ? wsc : hsc) * (1 << 16));
            if (!aaMenu[AA.None].Checked)
                divider /= 2;
            return Trunc_step((double)divider / (1 << 16));
        }
        /// <summary>
        /// Redraws picture box with gradient
        /// </summary>
        /// <param name="grad_width">Width of gradient picture box</param>
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
        /// <summary>
        /// Truncates given step so he could be added to complex coordinates without losing precision.
        /// </summary>
        /// <param name="in_step">Step value to be truncated</param>
        /// <returns>Truncated step value</returns>
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
        /// <summary>
        /// Shows the usage of double precision floating point format
        /// </summary>
        /// <param name="Real">Real part of complex coordinates</param>
        /// <param name="Imaginary">Imaginary part of complex coordinates</param>
        /// <param name="Step">Step between two adjacent pixels</param>
        private void UsageStatistics(double Real, double Imaginary, double Step)
        {
            //Get the maximum exponent of Im/Re parts of coordinates
            double expMS = Math.Log(Math.Abs(Math.Abs(Real) > Math.Abs(Imaginary) ? Real : Imaginary), 2);
            if (expMS >= 0)
                expMS++;
            //Difference between coordinates exponent and step exponent
            int ediff = (int)expMS - (int)Math.Log(Step, 2);
            //Since "Double" type contains 53 bits for mantissa, this is the maximum difference
            label1.Text = "Double usage: " + (ediff * 100 / 53f).ToString("F1") + "%";
        }
        /// <summary>
        /// Starts recalculating given area
        /// </summary>
        /// <param name="size">Size of the area</param>
        /// <param name="onlyColor">Set to true if user changed only gradient</param>
        /// <param name="update">Set to true if user changed only iterations count</param>
        /// <param name="noOutput">Set to true if statistics hasn't to be shown</param>
        /// <returns>Taks that can be awaited. Result is true when everything went fine</returns>
        private async Task<bool> RecalculateAsync(Size size, bool onlyColor = false, 
            bool update = false, bool noOutput = false)
        {
            if (pixelIterations.ForeColor != SystemColors.WindowText || !pictureBox1.Enabled)
                return false;
            pictureBox1.Enabled = false; //Prevents refreshing and disables any events
            fileMenu.Text = "Cancel";
            if (aaMenu[AA.SS4x].Checked) //Actual size: (2*W)x(2*H)
            {
                size.Width <<= 1;
                size.Height <<= 1;
            }
            else if (aaMenu[AA.HR2x].Checked) //Actual size: (W+1)x(2*H+1)
            {
                size.Width++;
                size.Height = (size.Height << 1) + 1;
                //[0,0];[2,0];[4,0]
                //[1,1];[3,1];[3,1]
                //[0,2];[2,2];[4,2]
            }
            int iterations = Convert.ToInt32(pixelIterations.Text);
            long total_iterations = 0;
            if (update) //Partial recalculation requested: obtain -(sum of iterations)
            {
                for (int xPixel = 0; xPixel < size.Width; xPixel++)
                    for (int yPixel = 0; yPixel < size.Height; yPixel++)
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
                progressBar1.Maximum = size.Width; //Equals to maximum number of columns in the array
                timer1.Start(); //This timer will throw event every 1/3s to refresh the progress bar
                FormBorderStyle = FormBorderStyle.FixedDialog;
                MaximizeBox = false;
                
                try
                {
                    _progress = 0;
                    CancellationTokenSource cts = new CancellationTokenSource();
                    _cancelWork = () =>
                    {
                        cts.Cancel();
                    };
                    await GenerateMandelbrot(size, update, cts.Token);
                }
                catch (Exception ex)
                {
                    while (ex is AggregateException && ex.InnerException != null)
                        ex = ex.InnerException;
                    MessageBox.Show(ex.Message, ex.Source);
                    return false;
                }
                finally
                {
                    _cancelWork = null;
                    wtime.Stop();
                }
            }
            if (!noOutput)
            {
                Rectangle bounds = saveFrame.Width > 0 ? new Rectangle(
                    (int)(pixel_iterations.GetLength(0) * saveFrame.X),
                    (int)(pixel_iterations.GetLength(1) * saveFrame.Y),
                    (int)(pixel_iterations.GetLength(0) * saveFrame.Width),
                    (int)(pixel_iterations.GetLength(1) * saveFrame.Height))
                    : new Rectangle(0, 0, 
                    pixel_iterations.GetLength(0), pixel_iterations.GetLength(1));
                if (gHistogram == null)
                {
                    long[] temp = null;
                    MakeHistogram(total_iterations, bounds, ref temp, onlyColor & !update);
                    FillBitmap(ConvertHistogram(temp), MakePalette(), null);
                }
                else
                    FillBitmap(gHistogram, MakePalette(), null);
                FinalizeRender(false);
            }
            return true;
        }
        /// <summary>
        /// Fills the histogram with count of pixels for each iteration value.
        /// </summary>
        /// <param name="total_iterations">When image being only updated, this function needs
        /// previous count of iterations for the whole image to calculate difference</param>
        /// <param name="bounds">Area that will be used in counting</param>
        /// <param name="histogram">Reference to array that stores count of iterations</param>
        /// <param name="onlyColor">Set to true if user only changed gradient</param>
        /// <param name="noOutput">Set to true if statistics hasn't to be shown</param>
        /// <param name="forceWG">Set to true when loading binary data from disc</param>
        private void MakeHistogram(long total_iterations, Rectangle bounds, ref long[] histogram, 
            bool onlyColor, bool noOutput = false, bool forceWG = false)
        {
            timer1.Stop();
            progressBar1.Value = 0;
            bool weighRend = renderMenu[0].Checked | forceWG;
            int i, j, iterations = prev_iterations;
            if (weighRend && histogram == null)
            {
                i = (int)Math.Log(iterations, 2) - 19;
                if (i > 0) //In case of >1M iterations histogram points will be packed
                    j = (i << 19) + (iterations >> i);
                else
                    j = iterations;
                histogram = new long[j + 2];
            }
            
            for (int xPixel = bounds.X; xPixel < bounds.Right; xPixel++)
            {
                int l;
                for (int yPixel = bounds.Y; yPixel < bounds.Bottom; yPixel++)
                {
                    j = (int)pixel_iterations[xPixel, yPixel]; //Integer count of iterations
                    //Actual iterations will be +3 because of double log during calculation
                    total_iterations += j + 3;
                    if (weighRend && j != iterations)
                    {
                        l = j;
                        if (l > (1 << 20)) //Pack iteration values >1M
                        {
                            j = (int)Math.Log(l, 2) - 19;
                            l = (j << 19) + ((l + (j >> 1)) >> j);
                        }
                        histogram[l]++; //Increment pixel count for each iteration number
                    }
                }
            }
            if (!noOutput)
            {
                string output;
                if (!onlyColor) //Show calculating time and performance in Gi per second
                {
                    output = string.Format("Mandel: {0:F2} s | {1:F2} Gips\r\n", wtime.ElapsedMilliseconds / 1000f,
                        (total_iterations / wtime.ElapsedMilliseconds) / 1e+6);
                }
                else //Show overall count of iterations for current picture
                    output = string.Format("Iterations sum: {0:G3}\r\n", (double)total_iterations);
                label7.Text = output;
                UsageStatistics(re, im, step);
            }
        }
        /// <summary>
        /// Converts histogram with absolute values to histogram with values in the range of [0…1]
        /// </summary>
        /// <param name="histogram">Array with absolute values</param>
        /// <returns>Array with values in the range of [0…1]</returns>
        private float[] ConvertHistogram(long[] histogram)
        {
            if (histogram == null)
                return null;
            //Each value will be the sum of all previous
            for (int i = 1; i < histogram.Length; i++) 
                histogram[i] += histogram[i - 1];
            float coloredPixels = histogram[histogram.Length - 1];
            //Make normalized histogram: each value will be in the range [0;1]
            float[] fHistogram = new float[histogram.Length];
            for (int i = 0; i < histogram.Length - 1; i++)
                fHistogram[i] = histogram[i] / coloredPixels;
            return fHistogram;
        }
        /// <summary>
        /// Creates gradient palette
        /// </summary>
        /// <returns>Gradien palette</returns>
        private PairGradient[] MakePalette()
        {
            int gwidth = filled_gradient.Width, i, j = 0, gradRM = pgrad_count - 1;
            float lborder = pgrad[0].position * gwidth, pg;
            if (lborder > 0)
                pg = 1f / ((1f - pgrad[gradRM].position + pgrad[0].position) * gwidth);
            else
                pg = 1f / ((pgrad[++j].position - pgrad[0].position) * gwidth);
            if (gPalette == null || gPalette.Length != gwidth)
                gPalette = new PairGradient[gwidth];
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
                gPalette[i].leftR = pgrad[k].color.R;
                gPalette[i].leftG = pgrad[k].color.G;
                gPalette[i].leftB = pgrad[k].color.B;
                k = (j == 0 || j > gradRM ? 0 : j);
                gPalette[i].diffR = pgrad[k].color.R - gPalette[i].leftR;
                gPalette[i].diffG = pgrad[k].color.G - gPalette[i].leftG;
                gPalette[i].diffB = pgrad[k].color.B - gPalette[i].leftB;
                gPalette[i].leftBorder = pg * (i - lborder) + (j == 0 ? 1f : 0f);
                gPalette[i].weight = pg;
            }
            return gPalette;
        }
        /// <summary>
        /// Calculates iterations within given area, starting at upper-left corner
        /// with coordinates (re, im).
        /// </summary>
        /// <param name="size">Size of area</param>
        /// <param name="totIters">Iterations limit</param>
        /// <param name="update">Only recalculate points with maximum iterations</param>
        /// <param name="token">Token that can cancel the task</param>
        /// <returns>Task that can be awaited</returns>
        private Task GenerateMandelbrot(Size size, bool update, CancellationToken token)
        {
            // info on mandelbrot and fractals
            // https://classes.yale.edu/fractals/MandelSet/welcome.html
            if (pixel_iterations.GetLength(0) != size.Width || size.Height != pixel_iterations.GetLength(1))
                pixel_iterations = new float[size.Width, size.Height];
            return Task.Factory.StartNew(() =>
            {
                int iterations = prev_iterations - 1, mode = extVer;
                if (update)
                    mode |= (1 << 30); //Set update flag
                if (aaMenu[AA.HR2x].Checked)
                    mode |= (1 << 29); //Set HRAA flag
                Parallel.For(0, size.Width,
                (xPixel, loopState) =>
                {
                    if (token.IsCancellationRequested)
                    {
                        loopState.Stop();
                        return;
                    }
                    if ((mode & 7) == 2)
                        Mandel64_AVX(ref pixel_iterations[xPixel, 0], re, im, step,
                        iterations, mode, size.Height, xPixel);
                    else
                        Mandel64_SSE2(ref pixel_iterations[xPixel, 0], re, im, step,
                        iterations, mode, size.Height, xPixel);
                    Interlocked.Increment(ref _progress);
                });
                token.ThrowIfCancellationRequested();
            }, token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }
        private void Timer1_Tick(object sender, EventArgs e)
        {
            //All threads of Parallel.For incrementing this global variable
            progressBar1.Value = _progress; //Equals to currently calculated columns
        }
        /// <summary>
        /// Converts pixel_iterations to bitmap image
        /// </summary>
        /// <param name="histogram">Histogram to be used to determine color for each pixel</param>
        /// <param name="palette">Gradient palette</param>
        /// <param name="temp">Temporary buffer for "casting" integer array to bitmap.
        /// Must be the same size as pixels in resulting image</param>
        private void FillBitmap(float[] histogram, PairGradient[] palette, int[] temp)
        {
            int width = pixel_iterations.GetLength(0), height = pixel_iterations.GetLength(1);
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
            
            Cursor.Current = Cursors.WaitCursor;
            wtime.Restart();

            bool isweighted = renderMenu[0].Checked;
            byte aamode = (byte)(aaMenu[AA.None].Checked ? 
                AA.None : (aaMenu[AA.HR2x].Checked ? AA.HR2x : AA.SS4x));
            int maxIters = prev_iterations -  1;
            int rrate = (textBox1.ForeColor == SystemColors.WindowText ?
                Convert.ToInt32(textBox1.Text) : maxIters);
            if (temp == null || temp.Length != width * height)
                temp = new int[width * height];
            Parallel.For(0, width, (xPixel) =>
            {
                float s = 0, vcR = 0, vcG = 0, vcB = 0;
                float[] sa = new float[5] { 0, 0, 0, 0, 0 };
                int k, p, yPixel = 0;
                int w = aamode == AA.HR2x ? 5 : aamode == AA.SS4x ? 4 : 1;
                byte[] icolor = new byte[4] { 0, 0, 0, 0xff };
                while (yPixel < height)
                {
                    vcR = vcG = vcB = 0;
                    if (aamode == AA.None)
                        sa[0] = pixel_iterations[xPixel, yPixel]; //Iterations for this pixel
                    else if (aamode == AA.SS4x) //4xSSAA: 4 values calculated for each pixel
                    {
                        k = yPixel << 1;
                        p = xPixel << 1;
                        sa[0] = pixel_iterations[p, k];
                        sa[1] = pixel_iterations[p, k + 1];
                        sa[2] = pixel_iterations[p + 1, k];
                        sa[3] = pixel_iterations[p + 1, k + 1];
                    }
                    else //2xHRAA: 5 values calculated for each pixel (K-type)
                    {
                        k = yPixel << 1;
                        p = xPixel;
                        sa[0] = pixel_iterations[p, k];
                        sa[1] = pixel_iterations[p, k + 2];
                        sa[2] = pixel_iterations[p + 1, k];
                        sa[3] = pixel_iterations[p + 1, k + 2];
                        sa[4] = pixel_iterations[p, k + 1];
                    }
                    for (p = 0; p < w; p++)
                    {
                        k = (int)(s = sa[p]);
                        if (k < maxIters - 1) //Index position in palette
                        {
                            if (isweighted)
                            {
                                if (k > (1 << 20))
                                {
                                    k = (int)Math.Log(k, 2) - 19;
                                    s = (k << 19) + s / (1 << k); //Unpacking iterations
                                    k = (int)s;
                                }
                                s = ((s - k) * (histogram[k + 1] - histogram[k]) + histogram[k]) * palette.Length;
                            }
                            else //Logarithmic coloring method
                                s = (float)(Math.Log(s / rrate + 1, 2) % 1) * palette.Length;
                            k = (int)s; //s range: [0; gwidth)
                        }
                        else
                            k = palette.Length;
                        if (k < palette.Length)
                        {
                            //Exact position between two colors in palette
                            s = (s - k) * palette[k].weight + palette[k].leftBorder;
                            if (s > 1) //Can occur because of float rounding errors
                                s = 1;
                            vcR += (palette[k].leftR + palette[k].diffR * s);
                            vcG += (palette[k].leftG + palette[k].diffG * s);
                            vcB += (palette[k].leftB + palette[k].diffB * s);
                        }
                        if (p == 3) //With 4xSSAA each value has 1/4 of total weight
                        {
                            vcR *= .25f;
                            vcG *= .25f;
                            vcB *= .25f;
                        }
                        else if (p == 4) //With 2xHRAA center value has 1/2 of total weight, 4 others has 1/8
                        {
                            vcR *= .5f;
                            vcG *= .5f;
                            vcB *= .5f;
                        }
                    }
                    icolor[2] = (byte)vcR;
                    icolor[1] = (byte)vcG;
                    icolor[0] = (byte)vcB;
                    //Write color in Argb format to pixel array
                    temp[xPixel + yPixel++ * width] = BitConverter.ToInt32(icolor, 0);
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
            Marshal.Copy(temp, 0, imageData.Scan0, image.Width * image.Height);
            image.UnlockBits(imageData);
            wtime.Stop();
            Cursor.Current = Cursors.Default;
            lr.Y = lr.X = 0;
            if (image.Width == pictureBox1.Width && pictureBox1.Height == image.Height)
                pictureBox1.Refresh();
        }
        /// <summary>
        /// Enables form resizing and picture box interaction. Statistics can be shown also
        /// </summary>
        /// <param name="noOutput">If set to true, statistics won't be show</param>
        private void FinalizeRender(bool noOutput)
        {
            pictureBox1.Enabled = true;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            if (!noOutput) //Show rendering time and performance in Mpixel per second
            {
                label7.Text += string.Format("Render: {0:F2} s | {1:F2} Mpixels", wtime.ElapsedMilliseconds / 1000f,
                    (image.Width * image.Height) / (wtime.ElapsedMilliseconds * 1000f));
                if (pictureBox1.ClientRectangle.Contains(pictureBox1.PointToClient(MousePosition)))
                    pictureBox1.Focus();
            }
            fileMenu.Text = "File menu";
        }
        /// <summary>
        /// Linearly interpolates the color between two samples
        /// </summary>
        /// <param name="A">First color</param>
        /// <param name="B">Second color</param>
        /// <param name="fraction">Weight of color B in the interpolation</param>
        /// <returns></returns>
        private Color Linear_interpolate(Color A, Color B, float fraction)
        {
            return Color.FromArgb(
                (byte)(A.R + (B.R - A.R) * fraction),
                (byte)(A.G + (B.G - A.G) * fraction),
                (byte)(A.B + (B.B - A.B) * fraction));
        }
        /// <summary>
        /// Shows options menu or cancels the ongoing calculation
        /// </summary>
        /// <param name="sender">Generic parameter</param>
        /// <param name="e">Generic parameter</param>
        private void FileMenu_Click(object sender, EventArgs e)
        {
            if (fileMenu.Text != "Cancel" && !IsFormOpen(typeof(PictureSave)))
                fileopts.Show(fileMenu, new Point(0, 0));
            else
                _cancelWork?.Invoke(); //Call delegate that will request cancellation
        }
        #region TextProcessing
        private void pixelIterations_TextChanged(object sender, EventArgs e)
        {
            try
            {
                if (Convert.ToInt32(pixelIterations.Text) < 16)
                    throw new FormatException();
                pixelIterations.ForeColor = SystemColors.WindowText;
                if (gHistogram != null)
                {
                    MessageBox.Show("Histogram is now unaware of binary data");
                    gHistogram = null;
                }
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
        #endregion TextProcessing
        private void PictureBox1_Paint(object sender, PaintEventArgs e)
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
            if (saveFrame.Width > 0)
            {
                e.Graphics.DrawRectangle(Pens.Pink, new Rectangle(
                    (int)(saveFrame.X * pictureBox1.Width), 
                    (int)(saveFrame.Y * pictureBox1.Height),
                    (int)(saveFrame.Width * pictureBox1.Width), 
                    (int)(saveFrame.Height * pictureBox1.Height)));
            }
        }
        /// <summary>
        /// Displays frame which represents the area of image that will be saved
        /// </summary>
        /// <param name="size">Size of image to be saved</param>
        /// <param name="result">Determines if user has closed the form with parameters or not</param>
        public void SaveFrameChanged(Size size, DialogResult result)
        {
            float pbs = pictureBox1.Width / (float)pictureBox1.Height;
            if (!size.IsEmpty)
            {
                double nRe, nIm, nSt;
                float fs = size.Width / (float)size.Height;
                int aa_fact = !aaMenu[AA.None].Checked ? 2 : 1;
                if (pbs > fs) //Frame will be fitted by height
                {
                    saveFrame.Height = 1;
                    saveFrame.Width = fs / pbs;
                    saveFrame.X = (1 - saveFrame.Width) * .5f;
                    saveFrame.Y = 0;
                    nRe = re + step * saveFrame.X * pictureBox1.Width * aa_fact;
                    nIm = im;
                    nSt = Trunc_step(step * pictureBox1.Height / size.Height);
                }
                else //Frame will be fitted by width
                {
                    saveFrame.Height = pbs / fs;
                    saveFrame.Width = 1;
                    saveFrame.X = 0;
                    saveFrame.Y = (1 - saveFrame.Height) * .5f;
                    nRe = re;
                    nIm = im + step * saveFrame.Y * pictureBox1.Height * aa_fact;
                    nSt = Trunc_step(step * pictureBox1.Width / size.Width);
                }
                UsageStatistics(nRe, nIm, nSt);
            }
            else
                saveFrame.Width = 0;
            if (result == DialogResult.OK)
                return;
            if (renderMenu[0].Checked) //Weighted rendering
            {
                Rectangle bounds = saveFrame.Width > 0 ? new Rectangle(
                   (int)(pixel_iterations.GetLength(0) * saveFrame.X),
                   (int)(pixel_iterations.GetLength(1) * saveFrame.Y),
                   (int)(pixel_iterations.GetLength(0) * saveFrame.Width),
                   (int)(pixel_iterations.GetLength(1) * saveFrame.Height))
                   : new Rectangle(0, 0,
                   pixel_iterations.GetLength(0), pixel_iterations.GetLength(1));
                if (gHistogram == null)
                {
                    long[] temp = null;
                    MakeHistogram(0, bounds, ref temp, true);
                    FillBitmap(ConvertHistogram(temp), MakePalette(), null);
                }
                else
                    FillBitmap(gHistogram, MakePalette(), null);
                FinalizeRender(false);
            }
            else
                pictureBox1.Refresh();
        }
        #region Main_picture_mouse_actions
        private void pictureBox1_MouseHover(object sender, EventArgs e)
        {
            pictureBox1.Focus();
        }
        private async void pictureBox1_MouseWheel(object sender, MouseEventArgs e)
        {
            if (pixelIterations.ForeColor == SystemColors.WindowText && pictureBox1.Enabled)
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
                    await RecalculateAsync(pictureBox1.Size);
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

        private async void pictureBox1_MouseUp(object sender, MouseEventArgs e)
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
                        await RecalculateAsync(pictureBox1.Size);
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
                        await RecalculateAsync(pictureBox1.Size, false, true);
                    }
                    else
                        await RecalculateAsync(pictureBox1.Size);
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
                await RecalculateAsync(pictureBox1.Size);
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
        private void PictureBox2_Paint(object sender, PaintEventArgs e)
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
            if (choosecolor.ShowDialog() == DialogResult.OK)
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
                    int i;
                    float j = (e.X - pictureBox2.Width / 42f) / filled_gradient.Width;
                    float d = (g_arrow.Width * .5f) / filled_gradient.Width;
                    selected_grad = -1;
                    for (i = 0; i < pgrad_count; i++)
                    {
                        if (j <= pgrad[i].position + d)
                        {
                            if (j >= pgrad[i].position - d) //If clicked on arrow, remember index
                                selected_grad = i;
                            break;
                        }
                    }
                    if (renderMenu[0].Checked && 
                        (1 > selected_grad || selected_grad + 2 > pgrad_count))
                        selected_grad = -1; //Disable movement of border points in weighted coloring
                }
                else if (renderMenu[1].Checked) //Upper half clicked while in logarithmic mode
                    shiftGrad = new Point(e.X, e.X);
            }
        }
        private void pictureBox2_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right && e.Y > pictureBox2.Height >> 1)
            {
                int i, l = -1;
                float j = (e.X - pictureBox2.Width / 42f) / filled_gradient.Width;
                float d = (g_arrow.Width * .5f) / filled_gradient.Width;
                if (j < -d)
                    j = -d;
                else if (j > 1f + d)
                    j = 1f + d;
                selected_grad = -1;
                nextposition = -1;
                for (i = 0; i < pgrad_count; i++)
                {
                    if (j <= pgrad[i].position + d)
                    {
                        if (j >= pgrad[i].position - d) //If clicked on arrow, remember index
                            selected_grad = i;
                        break;
                    }
                    else
                        l = i;
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
                    if (selected_grad < 0)
                    {
                        selected_grad = l;
                        if (j < 0.02f)
                            j = 0;
                        else if (j > 0.98f)
                            j = 1f;
                        nextposition = j;
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
                    float pos = (e.X - pictureBox2.Width / 42f) / filled_gradient.Width;
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
                else if (renderMenu[1].Checked)
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

        private async void Form1_Resize(object sender, EventArgs e)
        {
            if (WindowState != FormWindowState.Minimized && saveLoaded)
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
                        await RecalculateAsync(pictureBox1.Size);
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
            fileoptsMenu[2].Click += new EventHandler(SaveDialogOpen);
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
            extVer = GetLatestSupportedExtension();
            if (ModifierKeys == Keys.Shift)
                extVer = 1;
            if (extVer < extensionList.Length)
                Text += " " + extensionList[extVer];
            saveLoaded = true;
        }

        private void Form1_FormClosed(object sender, FormClosedEventArgs e)
        {
            FileSave(new SaveParams()
            {
                filename = "settings.jpg",
                tileX = 1,
                tileY = 1,
                size = pictureBox1.Size,
                guid = ImageFormat.Jpeg.Guid,
                quality = 75
            });
        }
        #endregion Main_form_events
        /// <summary>
        /// Shows the dialog and then metadata from selected image can be loaded via FileLoad
        /// </summary>
        /// <param name="sender">Generic parameter</param>
        /// <param name="e">Generic parameter</param>
        private void DataFileLoad(object sender, EventArgs e)
        {
            if (openFileDialog1.ShowDialog() == DialogResult.OK)
                FileOpen(openFileDialog1.FileName);
        }
        /// <summary>
        /// Shows the dialog and then preview image can be saved in JPG format
        /// </summary>
        /// <param name="sender">Generic parameter</param>
        /// <param name="e">Generic parameter</param>
        private void DataFileSave(object sender, EventArgs e)
        {
            saveFileDialog1.FileName = "settings_" + DateTime.Now.ToString("ddMMM_HH-mm");
            saveFileDialog1.Filter = "JPEG (*.jpg)|*.jpg";
            if (saveFileDialog1.ShowDialog() == DialogResult.OK)
                FileSave(new SaveParams()
                {
                    filename = saveFileDialog1.FileName,
                    tileX = 1,
                    tileY = 1,
                    size = pictureBox1.Size,
                    guid = ImageFormat.Jpeg.Guid,
                    quality = 75
                });
        }
        /// <summary>
        /// Shows non-modal form for editing parameters and then image can be saved
        /// </summary>
        /// <param name="sender">Generic parameter</param>
        /// <param name="e">Generic parameter</param>
        private void SaveDialogOpen(object sender, EventArgs e)
        {
            PictureSave psDiag = new PictureSave();
            saveParams.rawRequired = BinList.Count > 0;
            psDiag.sp = saveParams;
            psDiag.Show(this);
        }
        /// <summary>
        /// Generates and renders image(s) for saving. If size of picture box is the same as in parameters,
        /// then it'll be saved immediately via FileSave. If binary data is present, image(s) will be saved
        /// from that data. Otherwise recalculation will be performed.
        /// When saving tiled image, firstly binary files will be saved, then they'll be loaded and FileSave
        /// will be called for each. If calculation was skipped at some point, all generated files will be deleted.
        /// After all these actions, original image will be restored in the picture box
        /// </summary>
        /// <param name="sp">Paramaters for saving: size, format, filename, tiles, overlap</param>
        public async void PictureSaveAsync(SaveParams sp)
        {
            int aa_fact = aaMenu[AA.None].Checked ? 1 : 2;
            if (sp.size == image.Size && sp.tileX == 1)
            {
                FileSave(sp, true);
                if (sp.rawRequired)
                    await BinaryFileSaveAsync(sp.filename, new byte[pixel_iterations.Length * sizeof(float)]);
            }
            else //Redrawing of the picture required
            {
                //Save coordinates and array pointer
                oldStep = step;
                oldRe = re;
                oldIm = im;
                storePrev = pixel_iterations;
                //Calculate full size in pixels
                int fullWidth = sp.size.Width * sp.tileX + (int)(sp.size.Width * sp.overlap);
                int fullHeight = sp.size.Height * sp.tileY + (int)(sp.size.Height * sp.overlap);
                //Calculate tile size in pixels
                int tileWidth = sp.size.Width + (int)(sp.size.Width * sp.overlap);
                int tileHeight = sp.size.Height + (int)(sp.size.Height * sp.overlap);
                //Maximum scale calculation
                float sWidth = image.Width / (float)fullWidth;
                float sHeight = image.Height / (float)fullHeight;
                if (sWidth > sHeight)
                    sWidth = sHeight;
                //New coordinates of picture's upper-left corner
                step = Trunc_step(sWidth * step);
                re += oldStep * (image.Width * aa_fact >> 1) - step * (fullWidth * aa_fact >> 1);
                im += step * (fullHeight * aa_fact >> 1) - oldStep * (image.Height * aa_fact >> 1);
                //Temporary array for bitmap image filling
                int[] pixelData = new int[tileWidth * tileHeight];
                long[] htemp = null;
                float[] histogram;
                Rectangle bounds;
                if (sp.tileX == 1 && sp.tileY == 1) //Single picture needs to be saved
                {
                    if (!BinList.Count.Equals(1)) //No binary data was loaded
                    {
                        if (await RecalculateAsync(new Size(tileWidth, tileHeight), false, false, true))
                        {
                            //Bounds is whole picture area
                            bounds = new Rectangle(new Point(0, 0),
                                new Size(pixel_iterations.GetLength(0), pixel_iterations.GetLength(1)));
                            MakeHistogram(0, bounds, ref htemp, false);
                            FillBitmap(ConvertHistogram(htemp), MakePalette(), pixelData);
                            FileSave(sp, true);
                            if (sp.rawRequired)
                            {
                                await BinaryFileSaveAsync(sp.filename, new byte[pixel_iterations.Length * sizeof(float)]);
                                BinList.Add(sp.filename);
                            }
                        }
                    }
                    else //Save picture from binary data
                    {
                        SaveParams t = sp;
                        await BinaryFileLoadAsync(BinList[0], new byte[pixel_iterations.Length * sizeof(float)]);
                        FillBitmap(gHistogram, MakePalette(), pixelData);
                        t.filename = Path.ChangeExtension(BinList[0], Path.GetExtension(sp.filename));
                        FileSave(t, true);
                    }
                }
                else
                {
                    int i = 0;
                    //For saving tiled image we'll need a directory
                    string shortName = Path.GetFileNameWithoutExtension(sp.filename);
                    string dirName = Path.Combine(Path.GetDirectoryName(sp.filename), shortName);
                    byte[] temp = null;
                    if (BinList.Count != sp.tileX * sp.tileY) //No valid binary data was found when the file was opened
                    {
                        Directory.CreateDirectory(dirName);
                        SaveParams x = sp;
                        x.quality = 75;
                        x.filename = Path.Combine(dirName, Path.ChangeExtension(Path.GetFileName(sp.filename), "jpg"));
                        x.guid = ImageFormat.Jpeg.Guid;
                        FileSave(x); //Save coordinates file first with necessary metadata
                        //Bounds shouln't contain overlapping area
                        bounds = new Rectangle(new Point(0, 0), new Size(
                            pixel_iterations.GetLength(0) - (int)(pixel_iterations.GetLength(0) * sp.overlap), 
                            pixel_iterations.GetLength(1) - (int)(pixel_iterations.GetLength(1) * sp.overlap)));
                        //Now for each tile binary data will be saved, along with histogram generation
                        for (i = 0; i < sp.tileX; i++)
                            for (int j = 0; j < sp.tileY; j++)
                            {
                                re = oldRe - step * ((fullWidth * aa_fact >> 1) - i * sp.size.Width * aa_fact)
                                    + oldStep * (image.Width * aa_fact >> 1);
                                im = oldIm + step * ((fullHeight * aa_fact >> 1) - j * sp.size.Height * aa_fact)
                                    - oldStep * (image.Height * aa_fact >> 1);
                                pictureBox1.Enabled = true; //RecalcutaleAsync requires enabled picture box to run
                                if (!await RecalculateAsync(new Size(tileWidth, tileHeight), false, false, true))
                                {
                                    i = sp.tileX;
                                    break;
                                }
                                MakeHistogram(0, bounds, ref htemp, false);
                                string fname = Path.Combine(dirName, string.Format("{0}-{1}{2}", j, i,
                                    Path.GetExtension(sp.filename)));
                                if (temp == null)
                                    temp = new byte[pixel_iterations.Length * sizeof(float)];
                                await BinaryFileSaveAsync(fname, temp);
                                BinList.Add(fname);
                                label7.Text += string.Format("Processed: {0}/{1}", i * sp.tileY + j + 1, sp.tileX * sp.tileY);
                            }
                    }
                    if (i == sp.tileX + 1) //Calculation was cancelled
                    {
                        foreach (var file in Directory.GetFiles(dirName))
                            File.Delete(file);
                        Directory.Delete(dirName);
                    }
                    else
                    {
                        histogram = gHistogram ?? ConvertHistogram(htemp);
                        MakePalette(); //Palette will be stored in global array
                        SaveParams t = sp;
                        if (aaMenu[AA.SS4x].Checked) //Actual size: (2*W)x(2*H)
                        {
                            tileWidth <<= 1;
                            tileHeight <<= 1;
                        }
                        else if (aaMenu[AA.HR2x].Checked) //Actual size: (W+1)x(2*H+1)
                        {
                            tileWidth++;
                            tileHeight = (tileHeight << 1) + 1;
                        }
                        if (pixel_iterations.GetLength(0) != tileWidth || pixel_iterations.GetLength(1) != tileHeight)
                            pixel_iterations = new float[tileWidth, tileHeight];
                        if (temp == null)
                            temp = new byte[pixel_iterations.Length * sizeof(float)];
                        //Load each binary file and save picture with the same name
                        foreach (var file in BinList)
                        {
                            await BinaryFileLoadAsync(file, temp);
                            FillBitmap(histogram, gPalette, pixelData);
                            t.filename = Path.ChangeExtension(file, Path.GetExtension(sp.filename));
                            FileSave(t, true);
                        }
                        MessageBox.Show(BinList.Count.ToString() + " images saved", "Success", MessageBoxButtons.OK);
                    }
                }
                re = oldRe; //Restore previous image coordinates
                im = oldIm;
                step = oldStep;
                pixel_iterations = storePrev; //Reference to the previous picture data
                storePrev = null; //Delete reference copy so GC will be able to delete main array
                bounds = new Rectangle(new Point(0, 0),
                    new Size(pixel_iterations.GetLength(0), pixel_iterations.GetLength(1)));
                htemp = null;
                MakeHistogram(0, bounds, ref htemp, true, true);
                FillBitmap(ConvertHistogram(htemp), MakePalette(), null);
                FinalizeRender(true);
            }
        }
        /// <summary>
        /// Finds image codec by GUID
        /// </summary>
        /// <param name="g">GUID of requested codec</param>
        /// <returns>Requested codec, or null if it doesn't exists</returns>
        private ImageCodecInfo FindEncoder(Guid g)
        {
            foreach (ImageCodecInfo info in ImageCodecInfo.GetImageEncoders())
                if (info.FormatID.Equals(g))
                    return info;
            return null;
        }
        /// <summary>
        /// Detects if specified form is currently opened
        /// </summary>
        /// <param name="form">Type of form to check</param>
        /// <returns>True if form is opened</returns>
        private bool IsFormOpen(Type form)
        {
            foreach (var x in Application.OpenForms)
                if (x.GetType().Equals(form))
                    return true;
            return false;
        }
        /// <summary>
        /// Opens JPG image with specific metadata in it. Also tries to load binary file(s).
        /// Then it calculates scale factor so image will fit the picture box size
        /// </summary>
        /// <param name="filename">
        /// Name of the file to be opened
        /// </param>
        private async void FileOpen(string filename)
        {
            Image metadata = null;
            try
            {
                metadata = Image.FromFile(filename);
            }
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
            bool oldVersion = BitConverter.ToSingle(propItem.Value, 0) == 1.04f;
            if (!oldVersion && BitConverter.ToSingle(propItem.Value, 0) != settingsVersion)
            {
                MessageBox.Show("Unsupported metadata");
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
            if (!oldVersion)
            {
                saveParams.tileX = propItem.Value[56];
                saveParams.tileY = propItem.Value[57];
                saveParams.overlap = BitConverter.ToSingle(propItem.Value, 58);
            }
            else
            {
                saveParams.tileX = 1;
                saveParams.tileY = 1;
                saveParams.overlap = 0;
            }
            saveParams.size = new Size(pWidth, pHeight);
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
            BinList.Clear();
            gHistogram = null;
            //Calculating size in pixels with overlap
            int sWidth = pWidth + (int)(pWidth * saveParams.overlap);
            int sHeight = pHeight + (int)(pHeight * saveParams.overlap);
            //Calculating size of array for iterations
            if (aaMenu[AA.HR2x].Checked)
            {
                sWidth++;
                sHeight = (sHeight << 1) + 1;
            }
            else if (aaMenu[AA.SS4x].Checked)
            {
                sWidth <<= 1;
                sHeight <<= 1;
            }
            try
            {
                //Bounds for calculating histogram, for iterations array
                Rectangle bounds = new Rectangle(new Point(0, 0), new Size(
                    sWidth - (int)(sWidth * saveParams.overlap), 
                    sHeight - (int)(sHeight * saveParams.overlap)));
                byte[] temp = null;
                long[] htemp = null;
                //Check size of iterations array
                if (pixel_iterations.GetLength(0) != sWidth || pixel_iterations.GetLength(1) != sHeight)
                    pixel_iterations = new float[sWidth, sHeight];
                //If opened picture is containing information about tiles
                if (saveParams.tileX > 1 || 1 < saveParams.tileY) 
                {
                    int i = 0;
                    //We'll try to open all binary files with iterations information
                    for (i = 0; i < saveParams.tileX; i++)
                        for (int j = 0; j < saveParams.tileY; j++)
                        {
                            string fname = Path.Combine(Path.GetDirectoryName(filename), 
                                string.Format("{0}-{1}.bin", j, i));
                            fstream = new FileStream(fname, FileMode.Open, FileAccess.Read);
                            if ((fstream.Length >> 2) != sWidth * sHeight) //Compare size in bytes
                            {
                                i = saveParams.tileX;
                                break;
                            }
                            //This buffer will be used to cast float array to byte array
                            if (temp == null)
                                temp = new byte[fstream.Length];
                            await fstream.ReadAsync(temp, 0, temp.Length);
                            Buffer.BlockCopy(temp, 0, pixel_iterations, 0, temp.Length);
                            fstream.Close();
                            //Force to make histogram of incoming data, so it'll be available anyway
                            MakeHistogram(0, bounds, ref htemp, false, true, true);
                            //Add name to list, so later we can create pictures from these binary files
                            BinList.Add(fname);
                        }
                    if (i == saveParams.tileX)
                    {
                        //All files were read successfully
                        gHistogram = ConvertHistogram(htemp);
                        MessageBox.Show("Histogram for weighted rendering is fixed to binary data");
                    }
                }
                else
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
                        //TODO: Probably needs some reviewing…
                        temp = new byte[fstream.Length];
                        await fstream.ReadAsync(temp, 0, (int)fstream.Length);
                        Buffer.BlockCopy(temp, 0, pixel_iterations, 0, temp.Length);
                        piFilled = true;
                    }
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
            //Calculating full size in pixels, so picture can be properly scaled
            pWidth = pWidth * saveParams.tileX + (int)(pWidth * saveParams.overlap);
            pHeight = pHeight * saveParams.tileY + (int)(pHeight * saveParams.overlap);
            if (!piFilled)
            {
                float xHeight = pHeight / (float)pictureBox1.Height, yWidth = pWidth / (float)pictureBox1.Width;
                if (yWidth > xHeight)
                    yWidth = xHeight;
                step = Trunc_step(yWidth * step);
                re -= step * (aaMenu[AA.None].Checked ? pictureBox1.Width >> 1 : pictureBox1.Width);
                im += step * (aaMenu[AA.None].Checked ? pictureBox1.Height >> 1 : pictureBox1.Height);
                pictureBox1.Enabled = true;
                await RecalculateAsync(pictureBox1.Size);
            }
            else
            {
                re -= step * (sWidth >> 1);
                im += step * (sHeight >> 1);
                prev_iterations = Convert.ToInt32(pixelIterations.Text);
                //int histBottom = prev_iterations;
                Rectangle bounds = new Rectangle(new Point(0, 0),
                    new Size(pixel_iterations.GetLength(0), pixel_iterations.GetLength(1)));
                long[] htemp = null;
                MakeHistogram(0, bounds, ref htemp, true);
                FillBitmap(ConvertHistogram(htemp), MakePalette(), null);
                FinalizeRender(false);
            }
        }
        /// <summary>
        /// Saves image from bitmap in a given format (JPG, TIFF or PNG)
        /// </summary>
        /// <param name="sp">Paramaters for saving: size, format, filename, tiles, overlap</param>
        /// <param name="fullSize">False means that image will be scaled down and saved in JPG format</param>
        private void FileSave(SaveParams sp, bool fullSize = false)
        {
            ImageCodecInfo codec = FindEncoder(sp.guid);
            EncoderParameters encParams = new EncoderParameters(1);
            encParams.Param[0] = new EncoderParameter(Encoder.Quality, sp.quality);
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
            propItem.Len = 62;
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
            BitConverter.GetBytes(sp.size.Width).CopyTo(propItem.Value, 24);
            BitConverter.GetBytes(sp.size.Height).CopyTo(propItem.Value, 28);
            int aaFact = !aaMenu[AA.None].Checked ? 2 : 1;
            BitConverter.GetBytes(re + step * (aaFact * sp.size.Width * sp.tileX >> 1)).CopyTo(propItem.Value, 32);
            BitConverter.GetBytes(im - step * (aaFact * sp.size.Height * sp.tileY >> 1)).CopyTo(propItem.Value, 40);
            BitConverter.GetBytes(step).CopyTo(propItem.Value, 48);
            propItem.Value[56] = sp.tileX;
            propItem.Value[57] = sp.tileY;
            BitConverter.GetBytes(sp.overlap).CopyTo(propItem.Value, 58);
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
                    result.Save(sp.filename, codec, encParams);
                    result.Dispose();
                }
                else
                {
                    image.SetPropertyItem(propItem);
                    image.Save(sp.filename, codec, encParams);
                }
            }
            catch (SystemException ex)
            {
                MessageBox.Show(ex.Message, ex.Source);
            }
        }
        /// <summary>
        /// Saves pixel_iterations array to binary file
        /// </summary>
        /// <param name="filename">Name of the file where data will be saved to</param>
        /// <param name="temp">Temporary byte array for "casting" float array
        /// Must be 4 times larger (size of float) than pixel_iterations</param>
        /// <returns>Task that can be awaited</returns>
        private async Task BinaryFileSaveAsync(string filename, byte[] temp)
        {
            FileStream fstream = null;
            try
            {
                fstream = new FileStream(Path.ChangeExtension(filename, "bin"), 
                    FileMode.Create, FileAccess.Write);
                Buffer.BlockCopy(pixel_iterations, 0, temp, 0, temp.Length);
                await fstream.WriteAsync(temp, 0, temp.Length);
            }
            catch (Exception e)
            {
                MessageBox.Show(e.Message, "Binary data cannot be saved!");
            }
            finally
            {
                if (fstream != null)
                    fstream.Close();
            }
        }
        /// <summary>
        /// Loads pixel_iterations array from binary file
        /// </summary>
        /// <param name="filename">Name of the file</param>
        /// <param name="temp">Temporary byte array for "casting" float array
        /// Must be 4 times larger (size of float) than pixel_iterations</param>
        /// <returns>Task that can be awaited</returns>
        private async Task BinaryFileLoadAsync(string filename, byte[] temp)
        {
            FileStream fstream = null;
            try
            {
                fstream = new FileStream(Path.ChangeExtension(filename, "bin"),
                    FileMode.Open, FileAccess.Read);
                await fstream.ReadAsync(temp, 0, temp.Length);
                Buffer.BlockCopy(temp, 0, pixel_iterations, 0, temp.Length);
            }
            catch (Exception e)
            {
                MessageBox.Show(e.Message, "Binary data cannot be loaded!");
            }
            finally
            {
                if (fstream != null)
                    fstream.Close();
            }
        }
    }
}