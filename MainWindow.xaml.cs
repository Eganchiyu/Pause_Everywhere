using OpenCvSharp;
using OpenCvSharp.Extensions;
using OpenCvSharp.WpfExtensions;
using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;

namespace Pause_Everywhere
{
    public partial class MainWindow : System.Windows.Window
    {
        private Mat _preparedMat;
        private byte[] _previousScreenHash;
        private readonly object _frameLock = new();
        private Task _precomputeTask;
        private volatile bool _precomputeRunning = true;
        private volatile bool _isProcessingHotkey = false;
        private volatile bool _needsRecalculation = true;

        const int HOTKEY_ID = 1;
        const uint MOD_CONTROL = 0x0002;
        const uint MOD_ALT = 0x0001;
        const uint VK_P = 0x50;

        private const double SCALE_FACTOR = 0.1;
        private const int CHANGE_THRESHOLD = 2;
        private const int SKIP_FRAMES = 5;

        private int _frameCounter = 0;

        [DllImport("user32.dll")]
        static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            StartBackgroundPrecompute();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            var handle = new WindowInteropHelper(this).Handle;
            RegisterHotKey(handle, HOTKEY_ID, MOD_CONTROL | MOD_ALT, VK_P);

            HwndSource source = HwndSource.FromHwnd(handle);
            source.AddHook(WndProc);
        }

        private void StartBackgroundPrecompute()
        {
            _precomputeTask = Task.Run(async () =>
            {
                while (_precomputeRunning)
                {
                    try
                    {
                        _frameCounter++;
                        if (_frameCounter < SKIP_FRAMES)
                        {
                            await Task.Delay(100);
                            continue;
                        }
                        _frameCounter = 0;

                        if (_isProcessingHotkey ||
                            Dispatcher.Invoke(() => Visibility == Visibility.Visible))
                        {
                            await Task.Delay(200);
                            continue;
                        }

                        var handle = new WindowInteropHelper(this).Handle;
                        var screen = System.Windows.Forms.Screen.FromHandle(handle);
                        var bounds = screen.Bounds;

                        byte[] currentHash = CalculatePerceptualHash(bounds);

                        bool needsBlur =
                            _needsRecalculation ||
                            _previousScreenHash == null ||
                            CalculateHashDifference(currentHash, _previousScreenHash) > CHANGE_THRESHOLD;

                        if (needsBlur)
                        {
                            using var mat = CaptureAndProcessScreen(bounds);
                            lock (_frameLock)
                            {
                                _preparedMat?.Dispose();
                                _preparedMat = mat.Clone();
                            }

                            _previousScreenHash = currentHash;
                            _needsRecalculation = false;
                        }

                        await Task.Delay(50);
                    }
                    catch
                    {
                        await Task.Delay(100);
                    }
                }
            });
        }

        // 4x4 aHash
        private byte[] CalculatePerceptualHash(Rectangle bounds)
        {
            const int W = 4;
            const int H = 4;

            using var bmp = new Bitmap(W, H, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Low;
                g.CopyFromScreen(bounds.X, bounds.Y, 0, 0, new System.Drawing.Size(W, H));
            }

            int[] gray = new int[W * H];
            int sum = 0;
            int idx = 0;

            for (int y = 0; y < H; y++)
            {
                for (int x = 0; x < W; x++)
                {
                    var p = bmp.GetPixel(x, y);
                    int gval = (p.R + p.G + p.B) / 3;
                    gray[idx++] = gval;
                    sum += gval;
                }
            }

            int avg = sum / gray.Length;
            ushort hash = 0;

            for (int i = 0; i < gray.Length; i++)
            {
                if (gray[i] >= avg)
                    hash |= (ushort)(1 << i);
            }

            return BitConverter.GetBytes(hash);
        }

        private int CalculateHashDifference(byte[] h1, byte[] h2)
        {
            if (h1 == null || h2 == null || h1.Length != h2.Length)
                return int.MaxValue;

            int diff = 0;
            for (int i = 0; i < h1.Length; i++)
                diff += Math.Abs(h1[i] - h2[i]);

            return diff;
        }

        private Mat CaptureAndProcessScreen(Rectangle bounds)
        {
            int w = bounds.Width;
            int h = bounds.Height;

            using var bmp = new Bitmap(w, h);
            using (var g = Graphics.FromImage(bmp))
            {
                g.CopyFromScreen(bounds.X, bounds.Y, 0, 0, bmp.Size);
            }

            using var full = BitmapConverter.ToMat(bmp);
            using var small = new Mat();

            Cv2.Resize(full, small,
                new OpenCvSharp.Size(w * SCALE_FACTOR, h * SCALE_FACTOR));

            Cv2.GaussianBlur(small, small, new OpenCvSharp.Size(0, 0), 15);

            var result = new Mat();
            Cv2.Resize(small, result, new OpenCvSharp.Size(w, h));

            return result;
        }

        private IntPtr WndProc(
            IntPtr hwnd,
            int msg,
            IntPtr wParam,
            IntPtr lParam,
            ref bool handled)
        {
            const int WM_HOTKEY = 0x0312;

            if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
            {
                _isProcessingHotkey = true;
                _needsRecalculation = true;

                Dispatcher.Invoke(() =>
                {
                    if (Visibility != Visibility.Visible)
                    {
                        Visibility = Visibility.Visible;
                        Activate();

                        var handle = new WindowInteropHelper(this).Handle;
                        var screen = System.Windows.Forms.Screen.FromHandle(handle);
                        var bounds = screen.Bounds;

                        using var mat = CaptureAndProcessScreen(bounds);
                        var src = mat.ToBitmapSource();
                        src.Freeze();
                        BackImage.Source = src;

                        _previousScreenHash = CalculatePerceptualHash(bounds);
                    }
                    else
                    {
                        Visibility = Visibility.Hidden;
                    }
                });

                Task.Delay(300).ContinueWith(_ => _isProcessingHotkey = false);
                handled = true;
            }

            return IntPtr.Zero;
        }

        protected override void OnClosed(EventArgs e)
        {
            _precomputeRunning = false;

            try { _precomputeTask?.Wait(500); } catch { }

            var handle = new WindowInteropHelper(this).Handle;
            UnregisterHotKey(handle, HOTKEY_ID);

            lock (_frameLock)
            {
                _preparedMat?.Dispose();
                _preparedMat = null;
            }

            base.OnClosed(e);
        }
    }
}
