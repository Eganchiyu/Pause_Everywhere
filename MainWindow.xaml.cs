using Microsoft.VisualBasic;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using OpenCvSharp.WpfExtensions;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace Pause_Everywhere
{
    public partial class MainWindow : System.Windows.Window
    {
        private Mat _preparedMat = new Mat();// 预计算好的模糊图像（OpenCV矩阵）
        private byte[]? _previousScreenHash = null;// 上一帧的屏幕哈希值（用于变化检测）
        private readonly object _frameLock = new();// 线程锁，保护_preparedMat的访问
        private Task? _precomputeTask;// 后台预计算任务
        private volatile bool _precomputeRunning = true;// 控制后台任务运行
        private volatile bool _isProcessingHotkey = false;// 防止热键重复处理
        private volatile bool _needsRecalculation = true;// 标记需要重新计算模糊
        private Rectangle _screenBounds; // 屏幕区域的缓存

        const int HOTKEY_ID = 1;// 热键ID
        const uint MOD_CONTROL = 0x0002;// Ctrl键
        const uint MOD_ALT = 0x0001;// Alt键
        const uint VK_P = 0x50;// P键

        private const double SCALE_FACTOR = 0.1;// 缩放因子（缩小10倍处理）
        private const int CHANGE_THRESHOLD = 2;// 哈希差异阈值（>2视为屏幕变化）
        private const int SKIP_FRAMES = 5;// 跳帧数，降低CPU使用率

        private int _frameCounter = 0;

        [DllImport("user32.dll")]
        static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        //初始化阶段
        public MainWindow()
        {
            System.Media.SystemSounds.Beep.Play();
            Debug.WriteLine("初始化窗口");
            InitializeComponent();
            Debug.WriteLine("注册窗口加载事件");
            Loaded += MainWindow_Loaded;// 窗口加载完成后启动后台任务
        }

        // 窗口加载完成事件处理
        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Debug.WriteLine("开始运行预渲染");
            StartBackgroundPrecompute();
        }

        // 注册热键
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            var handle = new WindowInteropHelper(this).Handle;
            RegisterHotKey(handle, HOTKEY_ID, MOD_CONTROL | MOD_ALT, VK_P);

            // 获取屏幕区域的初始值的缓存
            var screen = Screen.FromHandle(handle);
            _screenBounds = screen.Bounds;

            HwndSource source = HwndSource.FromHwnd(handle);
            source.AddHook(WndProc);
        }

        // 启动后台预计算任务
        private void StartBackgroundPrecompute()
        {
            _precomputeTask = Task.Run(async () =>
            {
                while (_precomputeRunning)
                {
                    int iteration = 0;
                    //Debug.WriteLine($"=== 后台任务循环 #{iteration} ===");
                    try
                    {
                        // 跳帧处理，降低CPU使用率
                        _frameCounter++;
                        if (_frameCounter < SKIP_FRAMES)
                        {
                            await Task.Delay(100);
                            continue;
                        }
                        _frameCounter = 0;

                        //Debug.WriteLine("开始计算...");
                        // 如果正在处理热键或窗口可见，跳过本次计算
                        if (_isProcessingHotkey ||
                            Dispatcher.Invoke(() => Visibility == Visibility.Visible))
                        {
                            await Task.Delay(200);
                            continue;
                        }

                        Debug.WriteLine("开始计算...");

                        var bounds = _screenBounds;

                        Debug.WriteLine("Calculating current screen hash...");
                        // 计算当前屏幕的感知哈希值
                        byte[] currentHash = CalculatePerceptualHash(bounds);


                        // 判断是否需要重新计算模糊图像
                        /*
                         * 条件（或）：
                         * 1.首次计算（_previousScreenHash为null）
                         * 2.标记需要重新计算（_needsRecalculation为true）
                         * 3.屏幕变化显著（哈希差异大于阈值）
                         */
                        Debug.WriteLine("开始计算...");
                        bool needsBlur =
                            _needsRecalculation ||
                            _previousScreenHash == null ||
                            CalculateHashDifference(currentHash, _previousScreenHash) > CHANGE_THRESHOLD;

                        // 如果需要，捕获屏幕并计算模糊图像
                        if (needsBlur)
                        {
                            Debug.WriteLine("Recalculating blurred background...");
                            using var mat = CaptureAndProcessScreen(bounds);
                            lock (_frameLock)
                            {
                                _preparedMat?.Dispose();
                                _preparedMat = mat.Clone();// 深拷贝模糊图像
                            }

                            _previousScreenHash = currentHash;
                            _needsRecalculation = false;
                        }
                        else
                        {
                            Debug.WriteLine("No significant change detected, skipping recalculation.");
                        }

                        await Task.Delay(100);
                    }
                    catch
                    {
                        await Task.Delay(100);
                    }
                }
            });
        }

        // 计算屏幕区域的感知哈希值
        private byte[] CalculatePerceptualHash(Rectangle bounds)
        {
            // 使用4x4像素的缩略图计算感知哈希
            const int W = 4;
            const int H = 4;


            Debug.WriteLine("Capturing screen for perceptual hash...");
            // 捕获屏幕区域到Bitmap
            using var bmp = new Bitmap(W, H, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Low;
                g.CopyFromScreen(bounds.X, bounds.Y, 0, 0, new System.Drawing.Size(W, H));
            }

            //Debug.WriteLine("Calculating perceptual hash...");

            // 计算灰度值并求平均
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


            // 计算平均灰度值并生成哈希
            int avg = sum / gray.Length;
            ushort hash = 0;

            // 生成16位哈希值
            for (int i = 0; i < gray.Length; i++)
            {
                if (gray[i] >= avg)
                    hash |= (ushort)(1 << i);
            }

            // 返回哈希值的字节数组
            Debug.WriteLine($"Calculated hash: {hash:X4}");
            return BitConverter.GetBytes(hash);
        }

        // 计算两个哈希值的差异
        private int CalculateHashDifference(byte[] h1, byte[] h2)
        {
            // 哈希值长度不同，视为最大差异
            if (h1 == null || h2 == null || h1.Length != h2.Length)
                return int.MaxValue;

            // 计算绝对差异总和
            int diff = 0;
            for (int i = 0; i < h1.Length; i++)
                diff += Math.Abs(h1[i] - h2[i]);

            // 返回差异值
            return diff;
        }

        // 捕获屏幕并处理图像
        private Mat CaptureAndProcessScreen(Rectangle bounds)
        {
            //const double SCALE_FACTOR = 0.25; // 示例缩放因子

            int w = bounds.Width;
            int h = bounds.Height;

            // 捕获屏幕
            using var bmp = new System.Drawing.Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            using var g = System.Drawing.Graphics.FromImage(bmp);
            g.CopyFromScreen(bounds.X, bounds.Y, 0, 0, new System.Drawing.Size(w, h));

            // 转换为Mat
            using var full = OpenCvSharp.Extensions.BitmapConverter.ToMat(bmp);

            // 计算缩放尺寸
            int smallW = (int)(w * SCALE_FACTOR);
            int smallH = (int)(h * SCALE_FACTOR);

            // 处理图像
            var result = new Mat();
            Cv2.Resize(full, result, new OpenCvSharp.Size(smallW, smallH));
            Cv2.GaussianBlur(result, result, new OpenCvSharp.Size(0, 0), 15);
            Cv2.Resize(result, result, new OpenCvSharp.Size(w, h));

            return result;
        }
        //const int WM_HOTKEY = 0x0312; // 热键消息
        //// 窗口消息处理函数，处理热键事件
        //private IntPtr WndProc(
        //    IntPtr hwnd,
        //    int msg,
        //    IntPtr wParam,
        //    IntPtr lParam,
        //    ref bool handled)
        //{


        //    if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
        //    {
        //        _isProcessingHotkey = true; // 标记正在处理热键,防止重复触发
        //        _needsRecalculation = true; // 标记需要重新计算模糊图像

        //        Dispatcher.Invoke(() =>
        //        {
        //            if (Visibility != Visibility.Visible)
        //            {
        //                // 显示窗口并更新背景图像
        //                Visibility = Visibility.Visible;
        //                Activate();

        //                // 获取当前屏幕区域
        //                var handle = new WindowInteropHelper(this).Handle;
        //                var screen = System.Windows.Forms.Screen.FromHandle(handle);
        //                var bounds = screen.Bounds;

        //                // 捕获屏幕并更新背景图像
        //                using var mat = CaptureAndProcessScreen(bounds);


        //                var src = mat.ToBitmapSource(); // 转换为WPF图像
        //                src.Freeze(); // 冻结以跨线程使用
        //                BackImage.Source = src;

        //                _previousScreenHash = CalculatePerceptualHash(bounds);
        //            }
        //            else
        //            {
        //                Visibility = Visibility.Hidden;
        //            }
        //        });

        //        Task.Delay(300).ContinueWith(_ => _isProcessingHotkey = false);
        //        handled = true;
        //    }

        //    return IntPtr.Zero;
        //}

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_HOTKEY = 0x0312; // 热键消息
            if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
            {
                _isProcessingHotkey = true;
                _needsRecalculation = true; // 标记需要重新计算模糊图像

                Dispatcher.Invoke(() =>
                {
                    if (Visibility != Visibility.Visible)
                    {
                        // 立即显示窗口
                        Visibility = Visibility.Visible;
                        Activate();

                        // 使用预计算的模糊图像（如果有）
                        BitmapSource? src = null;

                        // 读取预计算图像时加锁,防止数据竞争
                        lock (_frameLock)
                        {
                            if (!_preparedMat.Empty())// 检查预计算图像是否有效
                            {
                                src = _preparedMat.ToBitmapSource(); // 转换为WPF图像
                            }
                        }

                        // 如果预计算图像可用，直接使用
                        if (src != null)
                        {
                            src.Freeze();
                            BackImage.Source = src;// 设置背景图像
                        }
                        
                        //else
                        //{
                        //    // 否则实时计算（后备方案）
                        //    var handle = new WindowInteropHelper(this).Handle;
                        //    var screen = System.Windows.Forms.Screen.FromHandle(handle);
                        //    var bounds = screen.Bounds;
                        //    using var mat = CaptureAndProcessScreen(bounds);
                        //    src = mat.ToBitmapSource();
                        //    src.Freeze();
                        //    BackImage.Source = src;
                        //}

                        // 标记需要更新（为下次显示做准备）
                        //_needsRecalculation = true;
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
        // 窗口关闭时的清理工作
        protected override void OnClosed(EventArgs e)
        {
            _precomputeRunning = false;

            try { _precomputeTask?.Wait(500); } catch { }

            var handle = new WindowInteropHelper(this).Handle;
            UnregisterHotKey(handle, HOTKEY_ID);

            lock (_frameLock)
            {
                _preparedMat?.Dispose();
                _preparedMat = new Mat();
            }

            base.OnClosed(e);
        }
    }
}
