using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Bench
{
    public partial class Main : Form
    {
        readonly Stopwatch wtime = new Stopwatch();
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        readonly string[] extensionList = new string[] { "FPU", "SSE2", "AVX" };
        Action _cancelWork = null;
        int coreCount = 0, processorCount = 0;
        readonly int[] _progress = new int[3];
        readonly BenchData[] benchDatas = new BenchData[3];

        [DllImport("MandelCore64.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int GetLatestSupportedExtension();
        [DllImport("MandelCore64.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern void Mandel64_FPU(ref float Arr, double Re, double Im,
            double Step, int Iterations, int Mode, int Height, int xPixel);
        [DllImport("MandelCore64.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern void Mandel64_SSE2(ref float Arr, double Re, double Im,
            double Step, int Iterations, int Mode, int Height, int xPixel);
        [DllImport("MandelCore64.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern void Mandel64_AVX(ref float Arr, double Re, double Im,
            double Step, int Iterations, int Mode, int Height, int xPixel);
        public Main()
        {
            InitializeComponent();
            timer.Interval = 500;
            timer.Tick += Timer_Tick;
            cbImageSize.Items.Add(640);
            cbImageSize.Items.Add(800);
            cbImageSize.Items.Add(1280);
            cbImageSize.Items.Add(1920);
            cbImageSize.SelectedIndex = 1;
            foreach (var item in new ManagementObjectSearcher("Select * from Win32_Processor").Get())
            {
                coreCount += int.Parse(item["NumberOfCores"].ToString());
            }
            foreach (var item in new ManagementObjectSearcher("Select * from Win32_ComputerSystem").Get())
            {
                processorCount += int.Parse(item["NumberOfLogicalProcessors"].ToString());
            }
            Text += string.Format(" {0}C/{1}T", coreCount, processorCount);
        }
        private void Timer_Tick(object sender, EventArgs e)
        {
            int width = (int)cbImageSize.SelectedItem;
            labelProgress.Text = string.Join(Environment.NewLine, 
                _progress.Select(k => ((float)k / width).ToString("P0")));
        }

        private async void BtnStart_Click(object sender, EventArgs e)
        {
            if (btnStart.Text != "Cancel")
            {
                btnStart.Text = "Cancel";
                cbImageSize.Enabled = false;
                int width = (int)cbImageSize.SelectedItem;
                if (await RecalculateAsync(new Size(width, width * 9 / 16), cbSolid.Checked))
                {
                    dgv1.DataSource = benchDatas;
                    dgv1.Columns["Rate"].DefaultCellStyle.Format = "F3";
                    dgv1.Refresh();
                }
            }
            else
                _cancelWork?.Invoke(); //Call delegate that will request cancellation
        }

        /// <summary>
        /// Starts recalculating given area
        /// </summary>
        /// <param name="size">Size of the area</param>
        /// <returns>Taks that can be awaited. Result is true when everything went fine</returns>
        private async Task<bool> RecalculateAsync(Size size, bool solid)
        {
            float[,] pixel_iterations = new float[size.Width, size.Height];
            int iterations = solid ? 260416 : 14000041;
            float re = solid ? .1f : (float)BitConverter.Int64BitsToDouble(4600427021396220557);
            float im = solid ? .1f : (float)BitConverter.Int64BitsToDouble(-4626398759775308636);
            float step = (float)BitConverter.Int64BitsToDouble(4406358819009986560);
            Array.Clear(_progress, 0, _progress.Length);
            CancellationTokenSource cts = new CancellationTokenSource();
            _cancelWork = () => { cts.Cancel(); };
            CancellationToken token = cts.Token;
            int extVer = GetLatestSupportedExtension();
            
            for (int j = 0; j < _progress.Length; j++)
            {
                benchDatas[j] = null;
                if (j > 1 && (extVer & 7) != 2)
                    break;
                timer.Start(); //This timer will throw event every 1/2s to refresh progress
                wtime.Restart(); //Start time measurement
                try
                {
                    // info on mandelbrot and fractals
                    // https://classes.yale.edu/fractals/MandelSet/welcome.html
                    switch (extensionList[j])
                    {
                        case "FPU":
                            await Task.Factory.StartNew(() =>
                            {
                                int mode = j;
                                Parallel.For(0, size.Width,
                                (xPixel, loopState) =>
                                {
                                    if (token.IsCancellationRequested)
                                    {
                                        loopState.Stop();
                                        return;
                                    }
                                    Mandel64_FPU(ref pixel_iterations[xPixel, 0], re, im, step,
                                        iterations, mode, size.Height, xPixel);
                                    Interlocked.Increment(ref _progress[0]);
                                });
                                token.ThrowIfCancellationRequested();
                            }, token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                            break;

                        case "SSE2":
                            await Task.Factory.StartNew(() =>
                            {
                                int mode = j;
                                Parallel.For(0, size.Width,
                                (xPixel, loopState) =>
                                {
                                    if (token.IsCancellationRequested)
                                    {
                                        loopState.Stop();
                                        return;
                                    }
                                    Mandel64_SSE2(ref pixel_iterations[xPixel, 0], re, im, step,
                                        iterations, mode, size.Height, xPixel);
                                    Interlocked.Increment(ref _progress[1]);
                                });
                                token.ThrowIfCancellationRequested();
                            }, token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                            break;

                        case "AVX":
                            await Task.Factory.StartNew(() =>
                            {
                                int mode = j;
                                Parallel.For(0, size.Width,
                                (xPixel, loopState) =>
                                {
                                    if (token.IsCancellationRequested)
                                    {
                                        loopState.Stop();
                                        return;
                                    }
                                    Mandel64_AVX(ref pixel_iterations[xPixel, 0], re, im, step,
                                        iterations, mode, size.Height, xPixel);
                                    Interlocked.Increment(ref _progress[2]);
                                });
                                token.ThrowIfCancellationRequested();
                            }, token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                            break;
                    }
                    
                }
                catch (Exception ex)
                {
                    while (ex is AggregateException && ex.InnerException != null)
                        ex = ex.InnerException;
                    MessageBox.Show(ex.Message, ex.Source);
                    break;
                }
                finally
                {
                    wtime.Stop();
                    timer.Stop();
                }
                //Do some calculations
                long total_iterations = 0;
                foreach (float f in pixel_iterations)
                    total_iterations += (long)f + 3;
                benchDatas[j] = new BenchData(extensionList[j], total_iterations, 
                    wtime.ElapsedMilliseconds, processorCount);
            }
            for (int j = 0; j < benchDatas.Length; j++)
            {
                if (benchDatas[j] != null)
                {
                    benchDatas[j].Rate = (double)benchDatas[j].GetPerf() / benchDatas[0].GetPerf();
                    if (benchDatas[j].Rate < (1 << j) * .75)
                        benchDatas[j].Instruction += " (fake)";
                }
            }
            _cancelWork = null;
            btnStart.Text = "Start";
            cbImageSize.Enabled = true;
            return true;
        }
    }
    public class BenchData
    {
        public string Instruction { get; set; }
        private readonly long perf;
        private readonly int threads;
        public long GetPerf() { return perf; }
        public string Performance {
            get {
                if (perf > 1e+6)
                    return (perf / 1e+6).ToString("F2") + " Gips";
                else
                    return (perf / 1e+3).ToString("F2") + " Mips";
            }
        }
        public string PerThread
        {
            get
            {
                if (perf / threads > 1e+6)
                    return (perf / threads / 1e+6).ToString("F2") + " Gips";
                else
                    return (perf / threads / 1e+3).ToString("F2") + " Mips";
            }
        }
        public double Rate { get; set; }
        public BenchData(string istructionSet, long totalIterations, long elapsedMs, int cpuThreads)
        {
            Instruction = istructionSet;
            perf = totalIterations / elapsedMs;
            threads = cpuThreads;
        }
    }
}
