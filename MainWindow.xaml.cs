using OpenCvSharp;
using OpenCvSharp.Extensions;
using OpenCvSharp.WpfExtensions;
//using System;
//using System.Drawing;
using System.Runtime.InteropServices;
//using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
//using System.Windows.Media.Imaging;

namespace Pause_Everywhere
{
    public partial class MainWindow : System.Windows.Window
    {
        // 使用 Mat 代替 BitmapSource，因为 Mat 是线程安全的
        private Mat _preparedMat;
        private byte[] _previousScreenHash; // 上次屏幕的哈希值
        private readonly object _frameLock = new();
        private Task _precomputeTask;
        private volatile bool _precomputeRunning = true;
        private volatile bool _isProcessingHotkey = false;
        private volatile bool _needsRecalculation = true;

        const int HOTKEY_ID = 1;
        const uint MOD_CONTROL = 0x0002;
        const uint MOD_ALT = 0x0001;
        const uint VK_P = 0x50;

        // 优化参数
        private const double SCALE_FACTOR = 0.1; // 更小的缩放比例，用于哈希计算
        private const int HASH_SIZE = 8; // 8x8的哈希
        private const int CHANGE_THRESHOLD = 10; // 哈希差异阈值
        private const int SKIP_FRAMES = 5; // 跳过更多帧

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
            // 窗口加载后开始预计算
            StartBackgroundPrecompute();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            var handle = new WindowInteropHelper(this).Handle;
            if (!RegisterHotKey(handle, HOTKEY_ID, MOD_CONTROL | MOD_ALT, VK_P))
            {
                System.Windows.MessageBox.Show("无法注册热键，可能已被其他程序占用。");
            }

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
                        // 帧跳过逻辑
                        _frameCounter++;
                        if (_frameCounter < SKIP_FRAMES)
                        {
                            await Task.Delay(100); // 更长的延迟
                            continue;
                        }
                        _frameCounter = 0;

                        // 只在窗口需要时捕获
                        if (!_isProcessingHotkey &&
                            !this.Dispatcher.Invoke(() => this.Visibility == Visibility.Visible))
                        {
                            await Task.Delay(200);
                            continue;
                        }

                        // 获取当前窗口所在的屏幕
                        var handle = new WindowInteropHelper(this).Handle;
                        var screen = System.Windows.Forms.Screen.FromHandle(handle);
                        var bounds = screen.Bounds;

                        // 计算当前屏幕的感知哈希
                        byte[] currentHash = CalculatePerceptualHash(bounds);

                        // 检查是否需要重新模糊
                        bool needsBlur = _needsRecalculation ||
                                        _previousScreenHash == null ||
                                        CalculateHashDifference(currentHash, _previousScreenHash) > CHANGE_THRESHOLD;

                        if (needsBlur)
                        {
                            // 使用更高效的捕获方法
                            using (var mat = CaptureAndProcessScreen_Perf(bounds))
                            {
                                lock (_frameLock)
                                {
                                    _preparedMat?.Dispose();
                                    _preparedMat = mat.Clone();
                                }
                            }

                            // 保存当前哈希
                            _previousScreenHash = currentHash;
                            _needsRecalculation = false;
                        }

                        // 如果窗口可见，立即更新显示
                        if (this.Dispatcher.Invoke(() => this.Visibility == Visibility.Visible))
                        {
                            UpdateImageFromPreparedMat();
                        }

                        await Task.Delay(50);
                    }
                    catch (Exception)
                    {
                        await Task.Delay(100);
                    }
                }
            });
        }

        // 计算感知哈希（非常轻量）
        private byte[] CalculatePerceptualHash(System.Drawing.Rectangle bounds)
        {
            // 更简单有效的哈希 - 使用屏幕缩略图
            using (var thumbnail = CaptureTinyThumbnail(bounds, 4, 4)) // 4x4像素
            {
                // 转换为灰度并计算平均值
                int total = 0;
                for (int y = 0; y < thumbnail.Height; y++)
                {
                    for (int x = 0; x < thumbnail.Width; x++)
                    {
                        var pixel = thumbnail.GetPixel(x, y);
                        total += (pixel.R + pixel.G + pixel.B) / 3;
                    }
                }

                // 返回平均亮度作为哈希
                byte avgBrightness = (byte)(total / (thumbnail.Width * thumbnail.Height));
                return [avgBrightness];
            }
        }

        // 捕获极小的缩略图
        private Bitmap CaptureTinyThumbnail(Rectangle bounds, int width, int height)
        {
            var bmp = new Bitmap(width, height);
            using (var g = Graphics.FromImage(bmp))
            {
                // 缩放到极小尺寸
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Low;
                g.DrawImage(CaptureScreenRegion(bounds, width * 10, height * 10),
                            0, 0, width, height);
            }
            return bmp;
        }

        // 捕获屏幕区域
        private Bitmap CaptureScreenRegion(Rectangle bounds, int width, int height)
        {
            var bmp = new Bitmap(width, height);
            using (var g = Graphics.FromImage(bmp))
            {
                g.CopyFromScreen(bounds.X, bounds.Y, 0, 0,
                    new System.Drawing.Size(width, height));
            }
            return bmp;
        }

        // 计算哈希差异
        private int CalculateHashDifference(byte[] hash1, byte[] hash2)
        {
            if (hash1 == null || hash2 == null || hash1.Length != hash2.Length)
                return int.MaxValue;

            int diff = 0;
            for (int i = 0; i < hash1.Length; i++)
            {
                diff += Math.Abs(hash1[i] - hash2[i]);
            }
            return diff;
        }

        // 高性能版本的屏幕捕获和处理
        private Mat CaptureAndProcessScreen_Perf(System.Drawing.Rectangle bounds)
        {
            var screenWidth = bounds.Width;
            var screenHeight = bounds.Height;
            var smallWidth = (int)(screenWidth * SCALE_FACTOR);
            var smallHeight = (int)(screenHeight * SCALE_FACTOR);

            using (var bmp = new Bitmap(screenWidth, screenHeight))
            using (var g = Graphics.FromImage(bmp))
            {
                g.CopyFromScreen(bounds.X, bounds.Y, 0, 0, bmp.Size);

                using (var fullMat = BitmapConverter.ToMat(bmp))
                using (var smallMat = new Mat())
                {
                    Cv2.Resize(fullMat, smallMat,
                        new OpenCvSharp.Size(smallWidth, smallHeight),
                        0, 0, InterpolationFlags.Linear);

                    Cv2.GaussianBlur(smallMat, smallMat,
                        new OpenCvSharp.Size(0, 0), 15);

                    using (var resultMat = new Mat())
                    {
                        Cv2.Resize(smallMat, resultMat,
                            new OpenCvSharp.Size(screenWidth, screenHeight),
                            0, 0, InterpolationFlags.Linear);

                        return resultMat.Clone();
                    }
                }
            }
        }

        // 在UI线程更新图像
        private void UpdateImageFromPreparedMat()
        {
            Dispatcher.Invoke(() =>
            {
                if (this.Visibility != Visibility.Visible || !IsLoaded)
                    return;

                try
                {
                    lock (_frameLock)
                    {
                        if (_preparedMat != null && !_preparedMat.IsDisposed)
                        {
                            var bitmapSource = _preparedMat.ToBitmapSource();
                            bitmapSource.Freeze();
                            BackImage.Source = bitmapSource;
                        }
                    }
                }
                catch (Exception)
                {
                    // 静默处理异常
                }
            });
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

                if (this.Visibility != Visibility.Visible)
                {
                    this.Dispatcher.Invoke(() =>
                    {
                        this.Visibility = Visibility.Visible;
                        this.Activate();
                        this.Topmost = true;
                        this.Topmost = false;

                        var handle = new WindowInteropHelper(this).Handle;
                        var screen = System.Windows.Forms.Screen.FromHandle(handle);
                        var bounds = screen.Bounds;

                        try
                        {
                            using (var mat = CaptureAndProcessScreen_Perf(bounds))
                            {
                                var bitmapSource = mat.ToBitmapSource();
                                bitmapSource.Freeze();
                                BackImage.Source = bitmapSource;

                                // 计算并保存哈希
                                _previousScreenHash = CalculatePerceptualHash(bounds);
                            }
                        }
                        catch (Exception)
                        {
                            // 静默处理异常
                        }
                    });
                }
                else
                {
                    this.Dispatcher.Invoke(() =>
                    {
                        this.Visibility = Visibility.Hidden;
                    });
                }

                Task.Delay(500).ContinueWith(_ => _isProcessingHotkey = false);

                handled = true;
            }

            return IntPtr.Zero;
        }

        protected override void OnClosed(EventArgs e)
        {
            _precomputeRunning = false;

            try
            {
                _precomputeTask?.Wait(1000);
            }
            catch { }

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