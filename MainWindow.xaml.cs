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
        // ===== 预计算模糊图像相关 =====
        private Mat _preparedMat = new Mat();// 预计算好的模糊图像（OpenCV矩阵）
        private byte[]? _previousScreenHash = null;// 上一帧的屏幕哈希值（用于变化检测）
        private readonly object _frameLock = new();// 线程锁，保护_preparedMat的访问
        private Task? _precomputeTask;// 后台预计算任务
        private volatile bool _precomputeRunning = true;// 控制后台任务运行
        private volatile bool _isProcessingHotkey = false;// 防止热键重复处理
        private volatile bool _needsRecalculation = true;// 标记需要重新计算模糊
        private Rectangle _screenBounds; // 屏幕区域的缓存
        // ===== 预计算模糊图像相关 =====



        //===== 热键注册相关 =====
        const int HOTKEY_ID = 1;// 热键ID
        const uint MOD_CONTROL = 0x0002;// Ctrl键
        const uint MOD_ALT = 0x0001;// Alt键
        const uint VK_P = 0x50;// P键
        //===== 热键注册相关 =====




        // ===== 屏幕捕获与处理相关 =====
        private const double SCALE_FACTOR = 0.1;// 缩放因子（缩小10倍处理）
        private const int CHANGE_THRESHOLD = 2;// 哈希差异阈值（>2视为屏幕变化）
        private const int SKIP_FRAMES = 5;// 跳帧数，降低CPU使用率

        private int _frameCounter = 0;
        // ===== 屏幕捕获与处理相关 =====



        // ===== 屏幕变化检测（低分辨率差分） =====
        const int DIFF_W = 64;
        const int DIFF_H = 36;

        // 上一帧灰度
        private byte[]? _prevGray = null;
        private bool _hasPrevGray = false;

        // 差分能量阈值（核心参数）
        private const int DIFF_ENERGY_THRESHOLD = 8000;

        // ===== 屏幕变化检测（低分辨率差分） =====

        // ===== 亮度判断参数 =====
        private const double BRIGHTNESS_THRESHOLD = 180; // 0~255，越大越“亮”
        private const double DIM_OPACITY = 0.25;          // 变暗强度
        private volatile bool _needDim = false;           // 是否需要变暗
        // ===== 亮度判断参数 =====

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
            Debug.WriteLine("开始运行预渲染主函数");
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

        /// <summary>
        /// 预计算任务主函数
        /// </summary>
        /// <remarks>启动异步循环，监控屏幕变化和程序状态，判定是否预计算。
        /// 该后台任务通过跳帧来降低 CPU 使用率，并在应用程序处理热键或窗口可见时避免重新计算。此方法应在内部调用，
        /// 用于管理预计算任务的生命周期。</remarks>
        private void StartBackgroundPrecompute()
        {
            _precomputeTask = Task.Run(async () =>
            {
                while (_precomputeRunning)
                {
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

                        //Debug.WriteLine("开始计算...");

                        var bounds = _screenBounds;
                        /*
                         * 判断是否需要重新计算模糊图像
                         * 条件1：标记需要重新计算
                         * 条件2：屏幕变化检测（低分辨率差分法）
                         */
                        //Debug.WriteLine("开始计算...");
                        bool needsBlur = _needsRecalculation || IsScreenChangedLowRes(bounds);


                        // 如果需要，捕获屏幕并计算模糊图像
                        if (needsBlur)
                        {
                            using var mat = CaptureAndProcessScreen(bounds);
                            lock (_frameLock)
                            {
                                _preparedMat?.Dispose();
                                _preparedMat = mat.Clone();
                            }
                            Debug.WriteLine("======最终结果：重新渲染======");
                            _needsRecalculation = false;
                        }
                        else
                        {
                            Debug.WriteLine("======最终结果：复用图像======");
                        }

                        await Task.Delay(100);
                    }
                    catch
                    {
                        Debug.WriteLine("预计算过程中出现异常，继续下一次循环");
                        await Task.Delay(100);
                    }
                }
            });
        }

        // 使用低分辨率差分法检测屏幕变化
        /// <summary>
        /// 低分辨率差分法
        /// </summary>
        /// <remarks>此方法通过将指定区域缩放到低分辨率并计算灰度值差异，将当前屏幕内容与前一帧进行比较。
        /// 针对显著变化优化，可以通过更改差分能量阈值<see cref="DIFF_ENERGY_THRESHOLD"/>来修改</remarks>
        /// <param name="bounds">目标屏幕区域，以屏幕坐标系中的矩形指定。</param>
        /// <returns>指定边界内是否发生显著变化</returns>
        private bool IsScreenChangedLowRes(Rectangle bounds)
        {
            // 1. 捕获整屏
            using var fullBmp = new Bitmap(
                bounds.Width,
                bounds.Height,
                System.Drawing.Imaging.PixelFormat.Format24bppRgb);

            using (var g = Graphics.FromImage(fullBmp))
            {
                g.CopyFromScreen(
                    bounds.X, bounds.Y,
                    0, 0,
                    bounds.Size,
                    CopyPixelOperation.SourceCopy);
            }

            // 2. 缩放到低分辨率
            using var smallBmp = new Bitmap(
                DIFF_W, DIFF_H,
                System.Drawing.Imaging.PixelFormat.Format24bppRgb);

            using (var g2 = Graphics.FromImage(smallBmp))
            {
                g2.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Low;
                g2.DrawImage(
                    fullBmp,
                    new Rectangle(0, 0, DIFF_W, DIFF_H));
            }

            // 3. 灰度 + 差分能量
            var currGray = new byte[DIFF_W * DIFF_H];
            int energy = 0;
            int idx = 0;

            for (int y = 0; y < DIFF_H; y++)
            {
                for (int x = 0; x < DIFF_W; x++)
                {
                    var p = smallBmp.GetPixel(x, y);
                    byte gval = (byte)((p.R + p.G + p.B) / 3);
                    currGray[idx] = gval;

                    if (_hasPrevGray)
                    {
                        energy += Math.Abs(gval - _prevGray![idx]);
                    }

                    idx++;
                }
            }

            _prevGray = currGray;
            _hasPrevGray = true;

            Debug.WriteLine($"低分辨率差分能量: {energy}");

            return energy > DIFF_ENERGY_THRESHOLD;
        }


        /// <summary>
        /// 捕获并处理指定屏幕区域的图像
        /// </summary>
        /// <remarks>
        /// 图像处理包括调整大小、高斯模糊和恢复原尺寸，用于降噪或减少细节。
        /// 调用方负责在使用完毕后释放返回的<see cref="Mat"/>对象。
        /// </remarks>
        /// <param name="bounds">要捕获的屏幕区域（屏幕坐标系）。</param>
        /// <returns>处理后的图像<see cref="Mat"/>对象。</returns>
        private Mat CaptureAndProcessScreen(Rectangle bounds)
        {
            int w = bounds.Width;
            int h = bounds.Height;

            // 捕获屏幕
            using var bmp = new System.Drawing.Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            using var g = System.Drawing.Graphics.FromImage(bmp);
            g.CopyFromScreen(bounds.X, bounds.Y, 0, 0, new System.Drawing.Size(w, h));

            // 转换为Mat
            using var full = OpenCvSharp.Extensions.BitmapConverter.ToMat(bmp);

            // 转灰度
            using var gray = new Mat();
            Cv2.CvtColor(full, gray, ColorConversionCodes.BGR2GRAY);

            // 计算平均亮度
            var meanScalar = Cv2.Mean(gray);
            double avgBrightness = meanScalar.Val0;

            // 更新是否需要变暗
            _needDim = avgBrightness > BRIGHTNESS_THRESHOLD;

            Debug.WriteLine($"平均亮度: {avgBrightness}, 需要变暗: {_needDim}");

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

        /// <summary>
        /// 窗口消息处理主函数，主要处理热键消息以控制窗口显示和更新背景
        /// </summary>
        /// <remarks>
        /// 此方法负责处理Windows消息循环中的WM_HOTKEY热键信息，
        /// 当检测到指定热键被按下时，会切换窗口的显示状态
        /// 消息处理后会将handled标志设为true，表示消息已被处理，不需要进一步传递。
        /// </remarks>
        /// <param name="hwnd">当前窗口的句柄</param>
        /// <param name="msg">Windows消息标识符</param>
        /// <param name="wParam">消息参数，对于热键消息表示热键ID</param>
        /// <param name="lParam">消息参数，内容取决于消息类型</param>
        /// <param name="handled">输出参数，指示消息是否已被处理</param>
        /// <returns>消息处理结果，此方法始终返回IntPtr.Zero</returns>
        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_HOTKEY = 0x0312; // 热键消息
            if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
            {
                _isProcessingHotkey = true;
                //_needsRecalculation = true; // 标记需要重新计算模糊图像

                Dispatcher.Invoke(() =>
                {
                    if (Visibility != Visibility.Visible)
                    {
                        if (_needDim)
                            DimLayer.Opacity = DIM_OPACITY;
                        else
                            DimLayer.Opacity = 0.0;

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

                        else
                        {
                            // 否则实时计算（后备方案）
                            var handle = new WindowInteropHelper(this).Handle;
                            var screen = System.Windows.Forms.Screen.FromHandle(handle);
                            var bounds = screen.Bounds;
                            using var mat = CaptureAndProcessScreen(bounds);
                            src = mat.ToBitmapSource();
                            src.Freeze();
                            BackImage.Source = src;
                        }
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
        /// <summary>
        /// 窗口关闭时执行清理操作
        /// </summary>
        /// <remarks>
        /// 此方法在窗口关闭时释放资源、注销热键并清理预计算数据。
        /// 作为窗口关闭流程的一部分被调用，确保资源正确释放。
        /// </remarks>
        /// <param name="e">包含事件数据的<see cref="EventArgs"/>对象</param>
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
